namespace DrumHero.Models;

public static class MidiDrumMap
{
    // Alesis Nitro Max default MIDI note mapping
    // Source: Alesis Nitro Max Drum Module User Guide v1.1, Section 5.2
    private static readonly Dictionary<int, DrumLane> _noteToLane = new()
    {
        // Kick
        { 36, DrumLane.LeftKick },
        
        // Snare (head and rim both map to snare lane)
        { 38, DrumLane.Snare },
        { 40, DrumLane.Snare },  // Snare Rim
        
        // Toms
        { 48, DrumLane.RackTom1 },     // Tom 1
        { 50, DrumLane.RackTom1 },     // Tom 1 Rim
        { 45, DrumLane.RackTom2 },     // Tom 2
        { 47, DrumLane.RackTom2 },     // Tom 2 Rim
        { 43, DrumLane.FloorTom },     // Tom 3 (floor tom)
        { 58, DrumLane.FloorTom },     // Tom 3 Rim
        { 41, DrumLane.FloorTom },     // Tom 4 (expansion, also floor tom)
        { 39, DrumLane.FloorTom },     // Tom 4 Rim
        
        // Hi-Hat
        { 42, DrumLane.ClosedHiHat },  // Hi-Hat Closed
        { 44, DrumLane.ClosedHiHat },  // Hi-Hat Pedal (foot chick)
        { 46, DrumLane.OpenHiHat },    // Hi-Hat Open
        { 23, DrumLane.OpenHiHat },    // Hi-Hat Half-Open
        { 21, DrumLane.OpenHiHat },    // Hi-Hat Splash
        
        // Cymbals
        { 49, DrumLane.Crash1 },       // Crash 1
        { 57, DrumLane.Crash2 },       // Crash 2
        { 55, DrumLane.Crash3 },       // Crash 3 (extra cymbal, China/Splash - MIDI 55 is common)
        { 52, DrumLane.Crash3 },       // Alternative crash 3 mapping
        { 51, DrumLane.Ride },         // Ride
        { 53, DrumLane.Ride },         // Ride Bell
        { 59, DrumLane.Ride },         // Ride Edge
    };

    public static DrumLane? GetLane(int midiNoteNumber)
    {
        return _noteToLane.TryGetValue(midiNoteNumber, out var lane) ? lane : null;
    }

    public static IReadOnlyDictionary<int, DrumLane> NoteToLaneMap => _noteToLane;
    
    // Reverse lookup: get the primary MIDI note for a lane (for transcription matching)
    private static readonly Dictionary<DrumLane, int> _laneToPrimaryNote = new()
    {
        { DrumLane.LeftKick, 36 },
        { DrumLane.RightKick, 36 },
        { DrumLane.Snare, 38 },
        { DrumLane.RackTom1, 48 },
        { DrumLane.RackTom2, 45 },
        { DrumLane.FloorTom, 43 },
        { DrumLane.ClosedHiHat, 42 },
        { DrumLane.OpenHiHat, 46 },
        { DrumLane.Crash1, 49 },
        { DrumLane.Crash2, 57 },
        { DrumLane.Crash3, 55 },
        { DrumLane.Ride, 51 },
    };
    
    public static int GetPrimaryNote(DrumLane lane) => _laneToPrimaryNote[lane];
}
