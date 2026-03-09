using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrumHero.Infrastructure;
using DrumHero.Models;
using DrumHero.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Windows;

namespace DrumHero.ViewModels;

public partial class LibraryViewModel : ViewModelBase
{
    private readonly SongLibraryService _libraryService;
    private readonly SongImportService _importService;
    private readonly NavigationService _navigation;
    private readonly Func<PracticeViewModel> _practiceVmFactory;
    private readonly Func<AnalysisProgressViewModel> _analysisVmFactory;

    [ObservableProperty]
    private ObservableCollection<Song> _songs = new();

    [ObservableProperty]
    private Song? _selectedSong;

    public LibraryViewModel(
        SongLibraryService libraryService,
        SongImportService importService,
        NavigationService navigation,
        Func<PracticeViewModel> practiceVmFactory,
        Func<AnalysisProgressViewModel> analysisVmFactory)
    {
        _libraryService = libraryService;
        _importService = importService;
        _navigation = navigation;
        _practiceVmFactory = practiceVmFactory;
        _analysisVmFactory = analysisVmFactory;
    }

    public override async Task OnNavigatedToAsync()
    {
        try
        {
            await LoadSongsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load songs: {ex.Message}\n{ex.StackTrace}");
            MessageBox.Show($"Error loading songs library:\n\n{ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task LoadSongsAsync()
    {
        var songs = await _libraryService.GetAllSongsAsync();
        Songs = new ObservableCollection<Song>(songs);
    }

    [RelayCommand]
    private async Task AddSongAsync()
    {
        // Step 1: Pick the FLAC audio file
        var flacDialog = new OpenFileDialog
        {
            Title = "Step 1/2: Select a FLAC audio file",
            Filter = "FLAC files (*.flac)|*.flac",
            CheckFileExists = true
        };

        if (flacDialog.ShowDialog() != true) return;

        var flacPath = flacDialog.FileName;
        var (isFlacValid, flacError) = _importService.ValidateFlacFile(flacPath);

        if (!isFlacValid)
        {
            MessageBox.Show(flacError!, "Invalid FLAC File", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Step 2: Pick the MIDI drum file (from Songsterr)
        var midiDialog = new OpenFileDialog
        {
            Title = "Step 2/2: Select the drum MIDI file (from Songsterr)",
            Filter = "MIDI files (*.mid;*.midi)|*.mid;*.midi",
            CheckFileExists = true
        };

        if (midiDialog.ShowDialog() != true) return;

        var midiPath = midiDialog.FileName;
        var (isMidiValid, midiError) = _importService.ValidateMidiFile(midiPath);

        if (!isMidiValid)
        {
            MessageBox.Show(midiError!, "Invalid MIDI File", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Navigate to analysis progress view with both file paths
        var analysisVm = _analysisVmFactory();
        analysisVm.SetFilePaths(flacPath, midiPath);
        analysisVm.AnalysisCompleted += async () =>
        {
            await _navigation.NavigateToAsync<LibraryViewModel>();
        };
        await _navigation.NavigateToAsync(analysisVm);
    }

    [RelayCommand]
    private async Task OpenSongAsync(Song? song)
    {
        var targetSong = song ?? SelectedSong;
        if (targetSong == null || targetSong.Status != SongStatus.Ready) return;

        var practiceVm = _practiceVmFactory();
        practiceVm.SetSong(targetSong);
        await _navigation.NavigateToAsync(practiceVm);
    }

    [RelayCommand]
    private async Task RemoveSongAsync(Song? song)
    {
        var targetSong = song ?? SelectedSong;
        if (targetSong == null) return;

        var result = MessageBox.Show(
            $"Remove \"{targetSong.Title}\" and all its data?\nThis cannot be undone.",
            "Confirm Removal",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        await _libraryService.DeleteSongAsync(targetSong.Id);
        Songs.Remove(targetSong);
        if (ReferenceEquals(SelectedSong, targetSong))
        {
            SelectedSong = null;
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        await _navigation.NavigateToAsync<SettingsViewModel>();
    }

    public string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"m\:ss");
    }
}
