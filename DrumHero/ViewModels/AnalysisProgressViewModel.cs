using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrumHero.Analysis;
using DrumHero.Infrastructure;
using DrumHero.Services;
using System.Windows;

namespace DrumHero.ViewModels;

public partial class AnalysisProgressViewModel : ViewModelBase
{
    private readonly SongImportService _importService;
    private CancellationTokenSource? _cts;
    private string _flacFilePath = string.Empty;
    private string _midiFilePath = string.Empty;

    [ObservableProperty]
    private string _songTitle = string.Empty;

    [ObservableProperty]
    private string _statusMessage = "Preparing analysis...";

    [ObservableProperty]
    private double _progressPercent;

    [ObservableProperty]
    private string _currentStage = string.Empty;

    [ObservableProperty]
    private bool _canCancel = true;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public event Action? AnalysisCompleted;

    public AnalysisProgressViewModel(SongImportService importService)
    {
        _importService = importService;
    }

    /// <summary>
    /// Sets both file paths for the import (FLAC audio + Songsterr MIDI).
    /// </summary>
    public void SetFilePaths(string flacFilePath, string midiFilePath)
    {
        _flacFilePath = flacFilePath;
        _midiFilePath = midiFilePath;
        var (title, artist, _) = _importService.ReadMetadata(flacFilePath);
        SongTitle = $"{title} - {artist}";
    }

    public override async Task OnNavigatedToAsync()
    {
        await StartAnalysisAsync();
    }

    private async Task StartAnalysisAsync()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        HasError = false;

        var progress = new Progress<AnalysisProgress>(p =>
        {
            ProgressPercent = p.ProgressFraction * 100;
            StatusMessage = p.Message;
            CurrentStage = p.Stage;
        });

        try
        {
            await _importService.ImportAsync(_flacFilePath, _midiFilePath, progress, _cts.Token);

            IsComplete = true;
            StatusMessage = "Analysis complete! Song is ready to practice.";
            ProgressPercent = 100;

            // Auto-navigate back after a brief delay
            await Task.Delay(1500);
            AnalysisCompleted?.Invoke();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Analysis cancelled.";
            HasError = true;
            ErrorMessage = "The analysis was cancelled.";
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ex.Message;
            StatusMessage = "Analysis failed. See detailed error below.";
            MessageBox.Show(
                ex.Message,
                "Analysis Error Details",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
            CanCancel = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        CanCancel = false;
        StatusMessage = "Cancelling...";
    }

    [RelayCommand]
    private void GoBack()
    {
        AnalysisCompleted?.Invoke();
    }
}
