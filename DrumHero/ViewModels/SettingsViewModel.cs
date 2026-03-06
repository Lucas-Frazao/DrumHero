using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrumHero.Audio;
using DrumHero.Infrastructure;
using DrumHero.Models;
using DrumHero.Services;
using System.Collections.ObjectModel;

namespace DrumHero.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly NavigationService _navigation;
    private AppSettings _settings = new();

    // Audio output
    [ObservableProperty]
    private ObservableCollection<DeviceInfo> _audioOutputDevices = new();

    [ObservableProperty]
    private DeviceInfo? _selectedAudioDevice;

    // MIDI input
    [ObservableProperty]
    private ObservableCollection<DeviceInfo> _midiInputDevices = new();

    [ObservableProperty]
    private DeviceInfo? _selectedMidiDevice;

    // Difficulty
    [ObservableProperty]
    private DifficultyLevel _difficulty = DifficultyLevel.Normal;

    [ObservableProperty]
    private string _difficultyDescription = string.Empty;

    // Audio buffer
    [ObservableProperty]
    private int _audioBufferSizeMs = 50;

    [ObservableProperty]
    private int _inputTimingOffsetMs = 45;

    // Count-in
    [ObservableProperty]
    private int _countInBars = 1;

    // API settings
    [ObservableProperty]
    private string _analysisApiUrl = string.Empty;

    [ObservableProperty]
    private string _analysisApiKey = string.Empty;

    // Status
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public Array DifficultyLevels => Enum.GetValues<DifficultyLevel>();

    public SettingsViewModel(SettingsService settingsService, NavigationService navigation)
    {
        _settingsService = settingsService;
        _navigation = navigation;
    }

    public override async Task OnNavigatedToAsync()
    {
        await LoadSettingsAsync();
        RefreshDevices();
    }

    private async Task LoadSettingsAsync()
    {
        _settings = await _settingsService.GetSettingsAsync();
        Difficulty = _settings.Difficulty;
        AudioBufferSizeMs = _settings.AudioBufferSizeMs;
        InputTimingOffsetMs = _settings.InputTimingOffsetMs;
        CountInBars = _settings.CountInBars;
        AnalysisApiUrl = _settings.AnalysisApiBaseUrl ?? "";
        AnalysisApiKey = _settings.AnalysisApiKey ?? "";
        UpdateDifficultyDescription();
    }

    [RelayCommand]
    private void RefreshDevices()
    {
        // Audio outputs
        var audioDevices = AudioPlaybackEngine.GetOutputDevices();
        AudioOutputDevices = new ObservableCollection<DeviceInfo>(
            audioDevices.Select(d => new DeviceInfo { Index = d.Index, Name = d.Name }));

        if (_settings.AudioOutputDeviceId != null)
        {
            SelectedAudioDevice = AudioOutputDevices.FirstOrDefault(
                d => d.Name == _settings.AudioOutputDeviceId);
        }
        SelectedAudioDevice ??= AudioOutputDevices.FirstOrDefault();

        // MIDI inputs
        var midiDevices = MidiInputEngine.GetMidiDevices();
        MidiInputDevices = new ObservableCollection<DeviceInfo>(
            midiDevices.Select(d => new DeviceInfo { Index = d.Index, Name = d.Name }));

        if (_settings.MidiInputDeviceId != null)
        {
            SelectedMidiDevice = MidiInputDevices.FirstOrDefault(
                d => d.Name == _settings.MidiInputDeviceId);
        }
        SelectedMidiDevice ??= MidiInputDevices.FirstOrDefault();
    }

    partial void OnDifficultyChanged(DifficultyLevel value)
    {
        UpdateDifficultyDescription();
    }

    private void UpdateDifficultyDescription()
    {
        DifficultyDescription = DifficultySettings.GetDisplayName(Difficulty);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _settings.AudioOutputDeviceId = SelectedAudioDevice?.Name;
        _settings.MidiInputDeviceId = SelectedMidiDevice?.Name;
        _settings.Difficulty = Difficulty;
        _settings.AudioBufferSizeMs = AudioBufferSizeMs;
        _settings.InputTimingOffsetMs = InputTimingOffsetMs;
        _settings.CountInBars = CountInBars;
        _settings.AnalysisApiBaseUrl = string.IsNullOrWhiteSpace(AnalysisApiUrl) ? null : AnalysisApiUrl;
        _settings.AnalysisApiKey = string.IsNullOrWhiteSpace(AnalysisApiKey) ? null : AnalysisApiKey;

        await _settingsService.SaveSettingsAsync(_settings);
        StatusMessage = "Settings saved.";
    }

    [RelayCommand]
    private async Task GoBackAsync()
    {
        await SaveAsync();
        await _navigation.NavigateToAsync<LibraryViewModel>();
    }
}

public class DeviceInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}
