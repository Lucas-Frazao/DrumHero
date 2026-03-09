using DrumHero.Analysis;
using DrumHero.Models;
using DrumHero.Persistence;
using System.IO;
using System.Text.Json;
using TagLib;

namespace DrumHero.Services;

public class SongImportService
{
    private readonly IAnalysisService _analysisService;
    private readonly SongRepository _songRepo;
    private readonly FileStorageService _fileStorage;

    public SongImportService(IAnalysisService analysisService, SongRepository songRepo, FileStorageService fileStorage)
    {
        _analysisService = analysisService;
        _songRepo = songRepo;
        _fileStorage = fileStorage;
    }

    /// <summary>
    /// Validates that the FLAC file is valid.
    /// </summary>
    public (bool IsValid, string? ErrorMessage) ValidateFlacFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return (false, "No file selected.");

        if (!System.IO.File.Exists(filePath))
            return (false, $"File not found: {filePath}");

        if (!filePath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase))
            return (false, "Only .flac files are supported.");

        return (true, null);
    }

    /// <summary>
    /// Validates that the MIDI file is valid.
    /// </summary>
    public (bool IsValid, string? ErrorMessage) ValidateMidiFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return (false, "No MIDI file selected.");

        if (!System.IO.File.Exists(filePath))
            return (false, $"File not found: {filePath}");

        if (!filePath.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) &&
            !filePath.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
            return (false, "Only .mid / .midi files are supported.");

        return (true, null);
    }

    /// <summary>
    /// Reads metadata from a FLAC file using TagLib.
    /// </summary>
    public (string Title, string Artist, double DurationSeconds) ReadMetadata(string filePath)
    {
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            var title = !string.IsNullOrWhiteSpace(tagFile.Tag.Title)
                ? tagFile.Tag.Title
                : Path.GetFileNameWithoutExtension(filePath);
            var artist = tagFile.Tag.FirstPerformer ?? tagFile.Tag.FirstAlbumArtist ?? "Unknown Artist";
            var duration = tagFile.Properties.Duration.TotalSeconds;
            return (title, artist, duration);
        }
        catch
        {
            return (Path.GetFileNameWithoutExtension(filePath), "Unknown Artist", 0);
        }
    }

    /// <summary>
    /// Imports a FLAC file (for audio) and a MIDI file (for drum highway).
    /// Runs Demucs stem separation on the FLAC, then parses the MIDI for the highway.
    /// </summary>
    public async Task<Song> ImportAsync(
        string flacFilePath,
        string midiFilePath,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Read metadata
        var (title, artist, duration) = ReadMetadata(flacFilePath);

        // Create initial song record
        var song = new Song
        {
            Title = title,
            Artist = artist,
            OriginalFilePath = flacFilePath,
            OriginalMidiFilePath = midiFilePath,
            DurationSeconds = duration,
            Status = SongStatus.Analyzing,
            DateProcessed = DateTime.Now,
        };

        await _songRepo.AddSongAsync(song);

        try
        {
            // Create output directory for this song
            var outputDir = _fileStorage.GetSongDirectory(song.Id);

            // Run analysis: Demucs for audio separation + MIDI parsing for highway
            var result = await _analysisService.AnalyzeAsync(
                flacFilePath, midiFilePath, outputDir, progress, cancellationToken);

            if (!result.Success)
            {
                await _songRepo.DeleteSongAsync(song.Id);
                _fileStorage.DeleteSongAssets(song.Id);
                throw new InvalidOperationException(result.ErrorMessage ?? "Analysis failed.");
            }

            // Read transcription to get BPM and time signature
            var transcriptionJson = await System.IO.File.ReadAllTextAsync(result.TranscriptionJsonPath, cancellationToken);
            var transcription = JsonSerializer.Deserialize<TranscriptionData>(transcriptionJson);

            if (transcription != null)
            {
                song.BPM = transcription.BPM;
                song.TimeSignatureNumerator = transcription.TimeSignature.Numerator;
                song.TimeSignatureDenominator = transcription.TimeSignature.Denominator;
                if (transcription.DurationSeconds > 0)
                    song.DurationSeconds = transcription.DurationSeconds;
            }

            // Store relative paths
            song.DrumlessTrackPath = GetRelativePath(result.DrumlessTrackPath, _fileStorage.BaseDataPath);
            song.DrumStemPath = GetRelativePath(result.DrumStemPath, _fileStorage.BaseDataPath);
            song.HighwayDataPath = GetRelativePath(result.TranscriptionJsonPath, _fileStorage.BaseDataPath);
            song.Status = SongStatus.Ready;

            await _songRepo.UpdateSongAsync(song);
            return song;
        }
        catch (Exception) when (song.Id > 0)
        {
            song.Status = SongStatus.Error;
            await _songRepo.UpdateSongAsync(song);
            throw;
        }
    }

    private static string GetRelativePath(string fullPath, string basePath)
    {
        if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath[(basePath.Length + 1)..];
        }
        return fullPath;
    }
}
