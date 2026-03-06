namespace DrumHero.Models;

public class PracticeRun
{
    public long Id { get; set; }
    public long SongId { get; set; }
    public DateTime Timestamp { get; set; }
    public double AccuracyPercent { get; set; }
    public int TotalNotes { get; set; }
    public int HitNotes { get; set; }
    public int MissedNotes { get; set; }
    public string Difficulty { get; set; } = "Normal";
    public double TempoPercent { get; set; } = 100.0;
    public double BPM { get; set; }
    public bool Completed { get; set; }  // true if played to end, false if stopped early
}
