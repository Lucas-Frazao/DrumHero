namespace DrumHero.Models;

public class Song
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string OriginalFilePath { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public double BPM { get; set; }
    public int TimeSignatureNumerator { get; set; } = 4;
    public int TimeSignatureDenominator { get; set; } = 4;
    public SongStatus Status { get; set; } = SongStatus.Importing;
    public DateTime DateProcessed { get; set; }
    public DateTime? LastPracticedDate { get; set; }
    
    // File references (relative paths within the app's data directory)
    public string DrumlessTrackPath { get; set; } = string.Empty;
    public string DrumStemPath { get; set; } = string.Empty;
    public string HighwayDataPath { get; set; } = string.Empty;
}

public enum SongStatus
{
    Importing,
    Analyzing,
    Ready,
    Error
}
