namespace DrumHero.Models;

public class HighwayNote
{
    public long Id { get; set; }
    public long SongId { get; set; }
    public DrumLane Lane { get; set; }
    public double TimeSeconds { get; set; }   // absolute time in seconds from song start
    public int Bar { get; set; }              // bar number (1-based)
    public int BeatInBar { get; set; }        // beat within bar (1-based)
    public int SubBeat { get; set; }          // sub-beat (1/32 note subdivision)
    public double Velocity { get; set; }      // 0.0 - 1.0 normalized velocity
    
    // Runtime state (not persisted)
    public NoteHitState HitState { get; set; } = NoteHitState.Pending;
    public double? HitTimeSeconds { get; set; }  // when the user actually hit it
}

public enum NoteHitState
{
    Pending,
    Hit,
    Missed
}
