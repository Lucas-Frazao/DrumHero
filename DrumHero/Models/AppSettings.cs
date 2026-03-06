namespace DrumHero.Models;

public class AppSettings
{
    public string? AudioOutputDeviceId { get; set; }
    public string? MidiInputDeviceId { get; set; }
    public DifficultyLevel Difficulty { get; set; } = DifficultyLevel.Normal;
    public string? AnalysisApiBaseUrl { get; set; }
    public string? AnalysisApiKey { get; set; }
    public int AudioBufferSizeMs { get; set; } = 50;
    public bool MetronomeEnabled { get; set; } = false;
    public int CountInBars { get; set; } = 1;  // 0 = no count-in, 1 or 2 bars
    public double MasterVolume { get; set; } = 0.8;
    public double MetronomeVolume { get; set; } = 0.5;
    public double DrumStemVolume { get; set; } = 0.0; // muted by default per spec
    public double BackingTrackVolume { get; set; } = 0.8;
    public int InputTimingOffsetMs { get; set; } = 45;
}
