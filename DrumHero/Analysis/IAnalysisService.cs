namespace DrumHero.Analysis;

/// <summary>
/// Interface for the analysis service that performs drum separation (via Demucs)
/// and MIDI-based transcription (from Songsterr MIDI files).
/// </summary>
public interface IAnalysisService
{
    /// <summary>
    /// Analyzes a FLAC audio file (stem separation) and a MIDI file (drum transcription)
    /// to produce a drumless backing track and highway transcription.
    /// </summary>
    /// <param name="flacFilePath">Absolute path to the input .flac file</param>
    /// <param name="midiFilePath">Absolute path to the drum MIDI file (from Songsterr)</param>
    /// <param name="outputDirectory">Directory where output files should be written</param>
    /// <param name="progress">Progress reporter (0.0 to 1.0)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result containing paths to generated files</returns>
    Task<AnalysisResult> AnalyzeAsync(
        string flacFilePath,
        string midiFilePath,
        string outputDirectory,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public class AnalysisResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string DrumlessTrackPath { get; set; } = string.Empty;
    public string DrumStemPath { get; set; } = string.Empty;
    public string TranscriptionJsonPath { get; set; } = string.Empty;
}

public class AnalysisProgress
{
    public string Stage { get; set; } = string.Empty;  // "Separating", "Transcribing", "Complete"
    public double ProgressFraction { get; set; }         // 0.0 - 1.0
    public string Message { get; set; } = string.Empty;
}
