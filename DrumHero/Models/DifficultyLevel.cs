namespace DrumHero.Models;

public enum DifficultyLevel
{
    Easy,
    Normal,
    Hard
}

public static class DifficultySettings
{
    /// <summary>
    /// Returns the hit window in milliseconds (±) for the given difficulty.
    /// </summary>
    public static double GetHitWindowMs(DifficultyLevel difficulty) => difficulty switch
    {
        DifficultyLevel.Easy => 95.0,
        DifficultyLevel.Normal => 65.0,
        DifficultyLevel.Hard => 40.0,
        _ => 65.0
    };

    public static string GetDisplayName(DifficultyLevel difficulty) => difficulty switch
    {
        DifficultyLevel.Easy => "Easy (±95ms)",
        DifficultyLevel.Normal => "Normal (±65ms)",
        DifficultyLevel.Hard => "Hard (±40ms)",
        _ => "Normal (±65ms)"
    };
}
