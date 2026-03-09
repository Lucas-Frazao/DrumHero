using DrumHero.Audio;
using DrumHero.Analysis;
using DrumHero.Models;
using DrumHero.Persistence;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace DrumHero.Services;

/// <summary>
/// Orchestrates a practice session: loads audio, manages playback, processes MIDI input,
/// coordinates hit detection, and records performance.
/// </summary>
public class PracticeSessionService : IDisposable
{
    private readonly AudioPlaybackEngine _audioEngine;
    private readonly MidiInputEngine _midiEngine;
    private readonly HitDetectionEngine _hitDetection;
    private readonly PracticeRunRepository _runRepo;
    private readonly FileStorageService _fileStorage;
    private readonly SongRepository _songRepo;
    private readonly IAnalysisService _analysisService;
    private readonly SettingsService _settingsService;

    private Song? _currentSong;
    private List<HighwayNote> _highwayNotes = new();
    private DifficultyLevel _difficulty = DifficultyLevel.Normal;
    private double _speedPercent = 100;
    private Stopwatch _sessionTimer = new();
    private double _playbackStartTime;
    private bool _isActive;
    private double _inputTimingOffsetSeconds = 0.045; // Positive value shifts judged hit earlier.

    // Public state
    public Song? CurrentSong => _currentSong;
    public List<HighwayNote> HighwayNotes => _highwayNotes;
    public bool IsPlaying => _audioEngine.IsPlaying;
    public bool IsActive => _isActive;
    public double CurrentTimeSeconds => _audioEngine.CurrentPosition.TotalSeconds;
    public double TotalDurationSeconds => _audioEngine.TotalDuration.TotalSeconds;
    public HitDetectionEngine HitDetection => _hitDetection;

    // Events
    public event Action<HighwayNote, double>? NoteHit;
    public event Action<HighwayNote>? NoteMissed;
    public event Action<DrumLane, double>? ExtraHit;
    public event Action? PlaybackEnded;
    /// <summary>Fires on every MIDI Note-On for diagnostic display: (midiNote, lane name or "unmapped")</summary>
    public event Action<int, string>? MidiNoteDebug;

    public PracticeSessionService(
        AudioPlaybackEngine audioEngine,
        MidiInputEngine midiEngine,
        HitDetectionEngine hitDetection,
        PracticeRunRepository runRepo,
        FileStorageService fileStorage,
        SongRepository songRepo,
        IAnalysisService analysisService,
        SettingsService settingsService)
    {
        _audioEngine = audioEngine;
        _midiEngine = midiEngine;
        _hitDetection = hitDetection;
        _runRepo = runRepo;
        _fileStorage = fileStorage;
        _songRepo = songRepo;
        _analysisService = analysisService;
        _settingsService = settingsService;

        // Wire up events
        _hitDetection.NoteHit += (note, timeDiff) => NoteHit?.Invoke(note, timeDiff);
        _hitDetection.NoteMissed += (note) => NoteMissed?.Invoke(note);
        _hitDetection.ExtraHit += (lane, time) => ExtraHit?.Invoke(lane, time);
        _midiEngine.NoteOnReceived += OnMidiNoteOn;
    }

    /// <summary>
    /// Loads a song for practice.
    /// </summary>
    public async Task LoadSongAsync(Song song, DifficultyLevel difficulty, double speedPercent = 100)
    {
        _currentSong = song;
        _difficulty = difficulty;
        _speedPercent = speedPercent;

        // Ensure the MIDI input device is open so we can receive hits
        await EnsureMidiDeviceOpenAsync();

        await EnsureSeparatedAssetsAsync(song);

        // Load highway data
        var highwayPath = _fileStorage.ResolveAssetPath(song.HighwayDataPath);
        var json = await System.IO.File.ReadAllTextAsync(highwayPath);
        var transcription = JsonSerializer.Deserialize<TranscriptionData>(json);

        if (transcription == null)
            throw new InvalidOperationException("Failed to load transcription data.");

        // Convert transcription notes to highway notes
        _highwayNotes = transcription.Notes
            .Select(n =>
            {
                var lane = MidiDrumMap.GetLane(n.MidiNote);
                if (lane == null) return null;
                return new HighwayNote
                {
                    SongId = song.Id,
                    Lane = lane.Value,
                    TimeSeconds = n.TimeSeconds,
                    Bar = n.Bar,
                    BeatInBar = n.Beat,
                    SubBeat = n.SubBeat,
                    Velocity = n.Velocity,
                };
            })
            .Where(n => n != null)
            .Cast<HighwayNote>()
            .OrderBy(n => n.TimeSeconds)
            .ToList();

        // Adjust note times for speed change
        if (Math.Abs(speedPercent - 100) > 0.01)
        {
            double speedFactor = speedPercent / 100.0;
            foreach (var note in _highwayNotes)
            {
                note.TimeSeconds /= speedFactor;
            }
        }

        // Initialize hit detection
        _hitDetection.Initialize(_highwayNotes, difficulty);

        // Ensure audio engine is initialized
        if (!_audioEngine.IsPlaying && _audioEngine.TotalDuration == TimeSpan.Zero)
        {
            _audioEngine.Initialize(latencyMs: 50);
        }

        // Load audio with time-stretch
        var backingPath = _fileStorage.ResolveAssetPath(song.DrumlessTrackPath);
        var drumStemPath = _fileStorage.ResolveAssetPath(song.DrumStemPath);
        var tempoChange = TimeStretchEngine.SpeedPercentToTempoChange(speedPercent);
        _audioEngine.LoadSession(backingPath, drumStemPath,
            tempoChangePercent: tempoChange);
    }

    private async Task EnsureSeparatedAssetsAsync(Song song)
    {
        var backingPath = _fileStorage.ResolveAssetPath(song.DrumlessTrackPath);
        var drumStemPath = _fileStorage.ResolveAssetPath(song.DrumStemPath);

        var requiresRepair =
            string.IsNullOrWhiteSpace(song.DrumlessTrackPath) ||
            string.IsNullOrWhiteSpace(song.DrumStemPath) ||
            string.Equals(backingPath, drumStemPath, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(backingPath) ||
            !File.Exists(drumStemPath);

        if (!requiresRepair)
            return;

        var sourceAudioPath = File.Exists(song.OriginalFilePath)
            ? song.OriginalFilePath
            : (File.Exists(backingPath) ? backingPath : drumStemPath);

        if (string.IsNullOrWhiteSpace(sourceAudioPath) || !File.Exists(sourceAudioPath))
            throw new InvalidOperationException("No valid source audio found to repair song stems.");

        // The MIDI file is required for the full analysis pipeline (stem separation + transcription).
        // If the original MIDI file is no longer available, we cannot fully repair.
        var midiPath = song.OriginalMidiFilePath;
        if (string.IsNullOrWhiteSpace(midiPath) || !File.Exists(midiPath))
            throw new InvalidOperationException(
                "Cannot repair song: the original Songsterr MIDI file is no longer available. " +
                "Please re-import the song with its MIDI file.");

        var outputDir = _fileStorage.GetSongDirectory(song.Id);
        var result = await _analysisService.AnalyzeAsync(sourceAudioPath, midiPath, outputDir);
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage ?? "Failed to rebuild drum stems.");

        song.DrumlessTrackPath = MakeRelativeToBaseDataPath(result.DrumlessTrackPath);
        song.DrumStemPath = MakeRelativeToBaseDataPath(result.DrumStemPath);
        song.HighwayDataPath = MakeRelativeToBaseDataPath(result.TranscriptionJsonPath);
        song.Status = SongStatus.Ready;

        if (File.Exists(result.TranscriptionJsonPath))
        {
            var json = await File.ReadAllTextAsync(result.TranscriptionJsonPath);
            var transcription = JsonSerializer.Deserialize<TranscriptionData>(json);
            if (transcription != null)
            {
                song.BPM = transcription.BPM;
                song.TimeSignatureNumerator = transcription.TimeSignature.Numerator;
                song.TimeSignatureDenominator = transcription.TimeSignature.Denominator;
                if (transcription.DurationSeconds > 0)
                    song.DurationSeconds = transcription.DurationSeconds;
            }
        }

        await _songRepo.UpdateSongAsync(song);
    }

    private string MakeRelativeToBaseDataPath(string fullPath)
    {
        if (fullPath.StartsWith(_fileStorage.BaseDataPath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath[(_fileStorage.BaseDataPath.Length + 1)..];
        }
        return fullPath;
    }

    public void Play()
    {
        _audioEngine.Play();
        _sessionTimer.Start();
        _isActive = true;
    }

    public void Pause()
    {
        _audioEngine.Pause();
        _sessionTimer.Stop();
    }

    public void Stop()
    {
        _audioEngine.Stop();
        _sessionTimer.Stop();
        _isActive = false;
    }

    public void Seek(double timeSeconds)
    {
        _audioEngine.Seek(TimeSpan.FromSeconds(timeSeconds));
        _hitDetection.Reset();
    }

    /// <summary>
    /// Call periodically (e.g., every frame) to update missed note detection.
    /// </summary>
    public void Update()
    {
        if (!_isActive) return;

        var currentTime = CurrentTimeSeconds;
        _hitDetection.UpdateMissedNotes(currentTime);

        // Check if playback has ended
        if (currentTime >= TotalDurationSeconds - 0.1 && TotalDurationSeconds > 0)
        {
            PlaybackEnded?.Invoke();
        }
    }

    private void OnMidiNoteOn(int midiNote, int velocity, double timestampMs)
    {
        var lane = MidiDrumMap.GetLane(midiNote);

        // Fire diagnostic event so the UI can show the raw MIDI note number
        MidiNoteDebug?.Invoke(midiNote, lane?.ToString() ?? "unmapped");

        if (lane == null) return;

        // Always trigger audible feedback regardless of play state —
        // the kit should feel like a real instrument at all times.
        _audioEngine.TriggerDrumSound(lane.Value, velocity);

        // Only process hit detection if actively playing
        if (!_isActive) return;

        var currentTime = CurrentTimeSeconds;
        var adjustedTime = Math.Max(0, currentTime - _inputTimingOffsetSeconds);
        _hitDetection.ProcessHit(lane.Value, adjustedTime, velocity);
    }

    public void SetInputTimingOffsetMs(int offsetMs)
    {
        _inputTimingOffsetSeconds = Math.Max(0, offsetMs) / 1000.0;
    }

    /// <summary>
    /// Saves the current session's performance as a PracticeRun.
    /// </summary>
    public async Task<PracticeRun> SavePerformanceAsync(bool completed)
    {
        _hitDetection.FinalizeSession();

        var run = new PracticeRun
        {
            SongId = _currentSong!.Id,
            Timestamp = DateTime.Now,
            AccuracyPercent = _hitDetection.AccuracyPercent,
            TotalNotes = _hitDetection.TotalNotes,
            HitNotes = _hitDetection.HitCount,
            MissedNotes = _hitDetection.MissCount,
            Difficulty = _difficulty.ToString(),
            TempoPercent = _speedPercent,
            BPM = _currentSong.BPM * (_speedPercent / 100.0),
            Completed = completed,
        };

        await _runRepo.AddRunAsync(run);
        return run;
    }

    public void SetBackingVolume(float volume)
    {
        _audioEngine.SetBackingVolume(volume);
    }

    public void SetDrumStemVolume(float volume)
    {
        _audioEngine.SetDrumStemVolume(volume);
    }

    public void SetDrumFeedbackVolume(float volume)
    {
        _audioEngine.SetDrumFeedbackVolume(volume);
    }

    public void SetMetronome(bool enabled, double bpm, int beatsPerBar, float volume)
    {
        _audioEngine.SetupMetronome(bpm * (_speedPercent / 100.0), beatsPerBar, volume);
        _audioEngine.SetMetronomeEnabled(enabled);
    }

    /// <summary>
    /// Opens the user's selected MIDI input device if it isn't already open.
    /// Reads the device selection from saved settings.
    /// </summary>
    private async Task EnsureMidiDeviceOpenAsync()
    {
        if (_midiEngine.IsOpen) return;

        var settings = await _settingsService.GetSettingsAsync();
        var devices = MidiInputEngine.GetMidiDevices();

        if (devices.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine("No MIDI input devices found.");
            return;
        }

        // Try to find the device matching saved settings
        int deviceIndex = 0; // default to first device
        if (!string.IsNullOrEmpty(settings.MidiInputDeviceId))
        {
            var match = devices.FirstOrDefault(d => d.Name == settings.MidiInputDeviceId);
            if (match != default)
            {
                deviceIndex = match.Index;
            }
        }

        try
        {
            _midiEngine.Open(deviceIndex);
            System.Diagnostics.Debug.WriteLine($"MIDI device opened: {devices[deviceIndex].Name}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open MIDI device: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _midiEngine.NoteOnReceived -= OnMidiNoteOn;
        _midiEngine.Close();
    }
}
