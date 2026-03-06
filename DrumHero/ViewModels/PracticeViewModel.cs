using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrumHero.Audio;
using DrumHero.Infrastructure;
using DrumHero.Models;
using DrumHero.Services;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace DrumHero.ViewModels;

public partial class PracticeViewModel : ViewModelBase
{
    private readonly PracticeSessionService _sessionService;
    private readonly SongLibraryService _libraryService;
    private readonly SettingsService _settingsService;
    private readonly NavigationService _navigation;
    private readonly DispatcherTimer _updateTimer;

    private Song? _song;

    // Song info
    [ObservableProperty] private string _songTitle = string.Empty;
    [ObservableProperty] private string _songArtist = string.Empty;
    [ObservableProperty] private double _songBpm;

    // Playback state
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _currentTimeSeconds;
    [ObservableProperty] private double _totalDurationSeconds;
    [ObservableProperty] private string _currentTimeDisplay = "0:00";
    [ObservableProperty] private string _totalTimeDisplay = "0:00";

    // Tempo control
    [ObservableProperty] private double _speedPercent = 100;
    [ObservableProperty] private double _currentBpm;
    [ObservableProperty] private int _selectedSpeedIndex = 3; // 100% by default
    [ObservableProperty] private bool _isPercentMode = true;

    // Metronome
    [ObservableProperty] private bool _metronomeEnabled;
    [ObservableProperty] private int _countInBars = 1;

    // Difficulty
    [ObservableProperty] private DifficultyLevel _difficulty = DifficultyLevel.Normal;

    // Performance tracking
    [ObservableProperty] private double _accuracyPercent;
    [ObservableProperty] private int _hitCount;
    [ObservableProperty] private int _missCount;
    [ObservableProperty] private int _totalNotes;

    // Volume controls
    [ObservableProperty] private double _backingVolume = 0.8;
    [ObservableProperty] private double _drumStemVolume = 0.0;
    [ObservableProperty] private double _metronomeVolume = 0.5;

    // Highway notes (for the renderer)
    public List<HighwayNote> HighwayNotes => _sessionService.HighwayNotes;

    // Performance summary
    [ObservableProperty] private bool _showPerformanceSummary;
    [ObservableProperty] private PracticeRun? _lastRun;

    // History
    [ObservableProperty] private ObservableCollection<PracticeRun> _practiceHistory = new();
    [ObservableProperty] private bool _showHistory;

    // Lane flash events for the highway renderer
    public event Action<DrumLane, bool>? LaneFlash; // (lane, isHit)
    public event Action? HighwayDataChanged;

    public double[] SpeedPresets => TimeStretchEngine.SpeedPresets;

    public PracticeViewModel(
        PracticeSessionService sessionService,
        SongLibraryService libraryService,
        SettingsService settingsService,
        NavigationService navigation)
    {
        _sessionService = sessionService;
        _libraryService = libraryService;
        _settingsService = settingsService;
        _navigation = navigation;

        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60fps
        };
        _updateTimer.Tick += OnUpdateTick;

        // Wire up hit detection events
        _sessionService.NoteHit += OnNoteHit;
        _sessionService.NoteMissed += OnNoteMissed;
        _sessionService.ExtraHit += OnExtraHit;
        _sessionService.PlaybackEnded += OnPlaybackEnded;
    }

    public void SetSong(Song song)
    {
        _song = song;
        SongTitle = song.Title;
        SongArtist = song.Artist;
        SongBpm = song.BPM;
        CurrentBpm = song.BPM;
        TotalDurationSeconds = song.DurationSeconds;
        TotalTimeDisplay = FormatTime(song.DurationSeconds);
    }

    public override async Task OnNavigatedToAsync()
    {
        if (_song == null) return;

        var settings = await _settingsService.GetSettingsAsync();
        Difficulty = settings.Difficulty;
        MetronomeEnabled = settings.MetronomeEnabled;
        CountInBars = settings.CountInBars;
        BackingVolume = settings.BackingTrackVolume;
        DrumStemVolume = settings.DrumStemVolume;
        MetronomeVolume = settings.MetronomeVolume;
        _sessionService.SetInputTimingOffsetMs(settings.InputTimingOffsetMs);

        await LoadSessionAsync();
        await LoadHistoryAsync();
    }

    private async Task LoadSessionAsync()
    {
        if (_song == null) return;

        IsBusy = true;
        BusyMessage = "Loading song...";

        try
        {
            await _sessionService.LoadSongAsync(_song, Difficulty, SpeedPercent);
            TotalNotes = _sessionService.HitDetection.TotalNotes;
            HighwayDataChanged?.Invoke();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to load song: {ex.Message}", "Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadHistoryAsync()
    {
        if (_song == null) return;
        var history = await _libraryService.GetPracticeHistoryAsync(_song.Id);
        PracticeHistory = new ObservableCollection<PracticeRun>(history);
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (IsPlaying)
        {
            _sessionService.Pause();
            _updateTimer.Stop();
            IsPlaying = false;
        }
        else
        {
            _sessionService.Play();
            _updateTimer.Start();
            IsPlaying = true;
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _sessionService.Stop();
        _updateTimer.Stop();
        IsPlaying = false;

        if (HitCount > 0 || MissCount > 0)
        {
            await SaveAndShowSummaryAsync(completed: false);
        }
    }

    [RelayCommand]
    private void Restart()
    {
        _sessionService.Seek(0);
        _sessionService.HitDetection.Reset();
        HitCount = 0;
        MissCount = 0;
        AccuracyPercent = 0;
        ShowPerformanceSummary = false;
        CurrentTimeSeconds = 0;
        CurrentTimeDisplay = "0:00";
    }

    [RelayCommand]
    private async Task SetSpeedAsync(double speedPercent)
    {
        SpeedPercent = speedPercent;
        CurrentBpm = SongBpm * (speedPercent / 100.0);

        // Reload session with new tempo
        await LoadSessionAsync();
    }

    [RelayCommand]
    private void AdjustBpm(int delta)
    {
        var newBpm = CurrentBpm + delta;
        if (newBpm < 20 || newBpm > 300) return;
        CurrentBpm = newBpm;
        SpeedPercent = (CurrentBpm / SongBpm) * 100.0;
    }

    [RelayCommand]
    private void ToggleMetronome()
    {
        MetronomeEnabled = !MetronomeEnabled;
        _sessionService.SetMetronome(MetronomeEnabled, SongBpm,
            _song?.TimeSignatureNumerator ?? 4, (float)MetronomeVolume);
    }

    [RelayCommand]
    private void ToggleHistory()
    {
        ShowHistory = !ShowHistory;
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        _sessionService.Stop();
        _updateTimer.Stop();
        await _navigation.NavigateToAsync<LibraryViewModel>();
    }

    [RelayCommand]
    private void DismissSummary()
    {
        ShowPerformanceSummary = false;
    }

    private void OnUpdateTick(object? sender, EventArgs e)
    {
        _sessionService.Update();

        CurrentTimeSeconds = _sessionService.CurrentTimeSeconds;
        CurrentTimeDisplay = FormatTime(CurrentTimeSeconds);

        // Update accuracy display
        HitCount = _sessionService.HitDetection.HitCount;
        MissCount = _sessionService.HitDetection.MissCount;
        AccuracyPercent = _sessionService.HitDetection.AccuracyPercent;
    }

    private void OnNoteHit(HighwayNote note, double timeDiffMs)
    {
        LaneFlash?.Invoke(note.Lane, true);
    }

    private void OnNoteMissed(HighwayNote note)
    {
        LaneFlash?.Invoke(note.Lane, false);
    }

    private void OnExtraHit(DrumLane lane, double time)
    {
        // Visual feedback for extra hits (not counted as miss)
        LaneFlash?.Invoke(lane, true);
    }

    private async void OnPlaybackEnded()
    {
        _updateTimer.Stop();
        IsPlaying = false;
        await SaveAndShowSummaryAsync(completed: true);
    }

    private async Task SaveAndShowSummaryAsync(bool completed)
    {
        try
        {
            LastRun = await _sessionService.SavePerformanceAsync(completed);
            await _libraryService.UpdateLastPracticedAsync(_song!.Id);
            await LoadHistoryAsync();
            ShowPerformanceSummary = true;
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to save performance: {ex.Message}");
        }
    }

    partial void OnBackingVolumeChanged(double value)
    {
        // Will be connected to audio engine in practice session
    }

    partial void OnDrumStemVolumeChanged(double value)
    {
        // Will be connected to audio engine in practice session
    }

    private static string FormatTime(double seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");
    }

    public override Task OnNavigatedFromAsync()
    {
        _updateTimer.Stop();
        _sessionService.Stop();
        return Task.CompletedTask;
    }
}
