using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DrumHero.Audio;

/// <summary>
/// Manages audio playback of backing track, drum stem, and metronome click.
/// Uses NAudio with WASAPI for low-latency output.
/// </summary>
public class AudioPlaybackEngine : IDisposable
{
    private IWavePlayer? _outputDevice;
    private MixingSampleProvider? _mixer;
    private AudioFileReader? _backingTrackReader;
    private AudioFileReader? _drumStemReader;
    private TimeStretchProvider? _backingTimeStretch;
    private TimeStretchProvider? _drumStemTimeStretch;
    private VolumeSampleProvider? _backingVolume;
    private VolumeSampleProvider? _drumStemVolume;
    private MetronomeProvider? _metronomeProvider;
    private VolumeSampleProvider? _metronomeVolume;
    private double _tempoChangePercent; // Current SoundTouch tempo change value
    
    private bool _isPlaying;
    private readonly object _lock = new();
    
    public bool IsPlaying => _isPlaying;
    /// <summary>
    /// Returns the current playback position in "heard" (wall-clock) time.
    /// When time-stretching, the underlying reader advances in source-sample time.
    /// At half speed (-50% tempo change), 4s of source audio takes 8s of wall time.
    /// Highway note times are scaled to wall-clock time, so we must convert:
    ///   heardTime = sourceTime / speedFactor
    /// where speedFactor = (100 + tempoChangePercent) / 100.
    /// </summary>
    public TimeSpan CurrentPosition
    {
        get
        {
            var raw = _backingTrackReader?.CurrentTime ?? TimeSpan.Zero;
            var speedFactor = (100.0 + _tempoChangePercent) / 100.0;
            if (speedFactor <= 0) speedFactor = 1.0; // safety
            return TimeSpan.FromTicks((long)(raw.Ticks / speedFactor));
        }
    }
    
    /// <summary>
    /// Total duration in "heard" (wall-clock) time at the current tempo.
    /// </summary>
    public TimeSpan TotalDuration
    {
        get
        {
            var raw = _backingTrackReader?.TotalTime ?? TimeSpan.Zero;
            var speedFactor = (100.0 + _tempoChangePercent) / 100.0;
            if (speedFactor <= 0) speedFactor = 1.0;
            return TimeSpan.FromTicks((long)(raw.Ticks / speedFactor));
        }
    }
    
    /// <summary>
    /// Initializes audio output device. Pass null for default WASAPI device.
    /// </summary>
    public void Initialize(int? deviceNumber = null, int latencyMs = 50)
    {
        Dispose();
        
        var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        var device = deviceNumber.HasValue
            ? enumerator.EnumerateAudioEndPoints(
                NAudio.CoreAudioApi.DataFlow.Render,
                NAudio.CoreAudioApi.DeviceState.Active)[deviceNumber.Value]
            : enumerator.GetDefaultAudioEndpoint(
                NAudio.CoreAudioApi.DataFlow.Render,
                NAudio.CoreAudioApi.Role.Multimedia);
        
        _outputDevice = new WasapiOut(
            device,
            NAudio.CoreAudioApi.AudioClientShareMode.Shared,
            false,
            latencyMs);
        
        // Create a mixer at 44100 Hz stereo
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))
        {
            ReadFully = true
        };
        
        _outputDevice.Init(_mixer);
    }
    
    /// <summary>
    /// Loads audio files for a practice session.
    /// </summary>
    /// <param name="tempoChangePercent">SoundTouch tempo change: 0 = normal, -50 = half speed, +100 = double speed.</param>
    public void LoadSession(string backingTrackPath, string drumStemPath,
        float backingVolume = 0.8f, float drumStemVolume = 0.0f,
        double tempoChangePercent = 0)
    {
        lock (_lock)
        {
            // Clean up previous
            ClearMixerInputs();
            _tempoChangePercent = tempoChangePercent;
            
            // Load backing track
            _backingTrackReader = new AudioFileReader(backingTrackPath);
            ISampleProvider backingChain = EnsureStereo44100(_backingTrackReader);
            _backingTimeStretch = new TimeStretchProvider(backingChain, tempoChangePercent);
            _backingVolume = new VolumeSampleProvider(_backingTimeStretch)
            {
                Volume = backingVolume
            };
            _mixer?.AddMixerInput(_backingVolume);
            
            // Load drum stem
            _drumStemReader = new AudioFileReader(drumStemPath);
            ISampleProvider drumChain = EnsureStereo44100(_drumStemReader);
            _drumStemTimeStretch = new TimeStretchProvider(drumChain, tempoChangePercent);
            _drumStemVolume = new VolumeSampleProvider(_drumStemTimeStretch)
            {
                Volume = drumStemVolume
            };
            _mixer?.AddMixerInput(_drumStemVolume);
        }
    }
    
    /// <summary>
    /// Updates the playback tempo at runtime without reloading the audio.
    /// </summary>
    /// <param name="tempoChangePercent">SoundTouch tempo change: 0 = normal, -50 = half speed, +100 = double speed.</param>
    public void SetTempoChange(double tempoChangePercent)
    {
        lock (_lock)
        {
            _tempoChangePercent = tempoChangePercent;
            _backingTimeStretch?.SetTempoChange(tempoChangePercent);
            _drumStemTimeStretch?.SetTempoChange(tempoChangePercent);
        }
    }
    
    /// <summary>
    /// Sets up metronome click aligned to tempo.
    /// </summary>
    public void SetupMetronome(double bpm, int beatsPerBar = 4, float volume = 0.5f)
    {
        lock (_lock)
        {
            if (_metronomeProvider != null && _mixer != null)
            {
                _mixer.RemoveMixerInput(_metronomeVolume!);
            }
            
            _metronomeProvider = new MetronomeProvider(bpm, beatsPerBar, 44100);
            _metronomeVolume = new VolumeSampleProvider(_metronomeProvider)
            {
                Volume = volume
            };
            _mixer?.AddMixerInput(_metronomeVolume);
        }
    }
    
    public void SetMetronomeEnabled(bool enabled)
    {
        if (_metronomeVolume != null)
            _metronomeVolume.Volume = enabled ? 0.5f : 0f;
    }
    
    public void SetBackingVolume(float volume)
    {
        if (_backingVolume != null) _backingVolume.Volume = volume;
    }
    
    public void SetDrumStemVolume(float volume)
    {
        if (_drumStemVolume != null) _drumStemVolume.Volume = volume;
    }
    
    public void Play()
    {
        _outputDevice?.Play();
        _isPlaying = true;
    }
    
    public void Pause()
    {
        _outputDevice?.Pause();
        _isPlaying = false;
    }
    
    public void Stop()
    {
        _outputDevice?.Stop();
        _isPlaying = false;
        Seek(TimeSpan.Zero);
    }
    
    public void Seek(TimeSpan position)
    {
        lock (_lock)
        {
            // position is in wall-clock (heard) time; convert to source time
            var speedFactor = (100.0 + _tempoChangePercent) / 100.0;
            if (speedFactor <= 0) speedFactor = 1.0;
            var sourcePosition = TimeSpan.FromTicks((long)(position.Ticks * speedFactor));
            
            if (_backingTrackReader != null)
                _backingTrackReader.CurrentTime = sourcePosition;
            if (_drumStemReader != null)
                _drumStemReader.CurrentTime = sourcePosition;
            _metronomeProvider?.Reset(position.TotalSeconds);
        }
    }
    
    private void ClearMixerInputs()
    {
        _mixer?.RemoveAllMixerInputs();
        _backingTrackReader?.Dispose();
        _drumStemReader?.Dispose();
        _backingTrackReader = null;
        _drumStemReader = null;
        _backingTimeStretch = null;
        _drumStemTimeStretch = null;
        _backingVolume = null;
        _drumStemVolume = null;
    }
    
    private static ISampleProvider EnsureStereo44100(ISampleProvider source)
    {
        var result = source;
        
        // Resample if needed
        if (source.WaveFormat.SampleRate != 44100)
        {
            result = new WdlResamplingSampleProvider(result, 44100);
        }
        
        // Convert to stereo if mono
        if (result.WaveFormat.Channels == 1)
        {
            result = new MonoToStereoSampleProvider(result);
        }
        
        return result;
    }
    
    public void Dispose()
    {
        _isPlaying = false;
        ClearMixerInputs();
        _outputDevice?.Dispose();
        _outputDevice = null;
        _mixer = null;
    }
    
    /// <summary>
    /// Gets available audio output devices.
    /// </summary>
    public static List<(int Index, string Name)> GetOutputDevices()
    {
        var devices = new List<(int, string)>();
        var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        var endpoints = enumerator.EnumerateAudioEndPoints(
            NAudio.CoreAudioApi.DataFlow.Render,
            NAudio.CoreAudioApi.DeviceState.Active);
        
        for (int i = 0; i < endpoints.Count; i++)
        {
            devices.Add((i, endpoints[i].FriendlyName));
        }
        return devices;
    }
}
