namespace DrumHero.Models;

public static class MidiDrumMap
{
    // Alesis Nitro Max MIDI note mapping (updated for user's kit configuration)
    // Source: Alesis Nitro Max Drum Module User Guide v1.1, Section 5.2
    // Note: User has reassigned Tom 4 input (default MIDI 41) to send MIDI 52 (China Cymbal)
    //       for extra cymbal 2. MIDI 41 is now free to map from Songsterr MIDIs to Floor Tom.
    private static readonly Dictionary<int, DrumLane> _noteToLane = new()
    {
        // Kick
        { 36, DrumLane.LeftKick },
        { 35, DrumLane.LeftKick },     // GM: Acoustic Bass Drum → Kick
        
        // Snare (head and rim both map to snare lane)
        { 38, DrumLane.Snare },        // Acoustic Snare
        { 40, DrumLane.Snare },        // Electric Snare / Snare Rim
        { 37, DrumLane.Snare },        // Side Stick → Snare
        { 39, DrumLane.Snare },        // Hand Clap → Snare
        
        // Toms
        { 48, DrumLane.RackTom1 },     // Hi-Mid Tom (Tom 1)
        { 50, DrumLane.RackTom1 },     // High Tom / Tom 1 Rim
        { 45, DrumLane.RackTom2 },     // Low Tom (Tom 2)
        { 47, DrumLane.RackTom2 },     // Low-Mid Tom / Tom 2 Rim
        { 43, DrumLane.FloorTom },     // High Floor Tom (Tom 3)
        { 58, DrumLane.FloorTom },     // Tom 3 Rim (Vibraslap in GM, but Alesis uses it as tom rim)
        { 41, DrumLane.FloorTom },     // Low Floor Tom → Floor Tom (no longer conflicts with cymbal)
        
        // Hi-Hat
        { 42, DrumLane.ClosedHiHat },  // Closed Hi-Hat
        { 44, DrumLane.ClosedHiHat },  // Pedal Hi-Hat (foot chick)
        { 46, DrumLane.OpenHiHat },    // Open Hi-Hat
        { 23, DrumLane.OpenHiHat },    // Hi-Hat Half-Open (Alesis-specific)
        { 21, DrumLane.OpenHiHat },    // Hi-Hat Splash (Alesis-specific)
        
        // Cymbals
        { 49, DrumLane.Crash1 },       // Crash Cymbal 1
        { 57, DrumLane.Crash2 },       // Crash Cymbal 2 (extra cymbal input)
        { 55, DrumLane.Crash3 },       // Splash Cymbal → Crash 3
        { 52, DrumLane.Crash3 },       // Chinese Cymbal → Crash 3 (user's Tom 4 input, reassigned to MIDI 52)
        { 51, DrumLane.Ride },         // Ride Cymbal 1
        { 53, DrumLane.Ride },         // Ride Bell
        { 59, DrumLane.Ride },         // Ride Cymbal 2 / Ride Edge
    };

    /// <summary>
    /// Maps a General MIDI percussion note number to the closest equivalent note number
    /// on the user's Alesis Nitro Max kit. This handles Songsterr MIDIs that may reference
    /// percussion instruments not physically present on the kit.
    /// 
    /// If the GM note is already in the kit's mapping, it is returned as-is.
    /// Otherwise, it is mapped to the closest equivalent.
    /// </summary>
    private static readonly Dictionary<int, int> _gmToKitFallback = new()
    {
        // These are GM percussion notes that are NOT directly in _noteToLane
        // but should be mapped to something on the kit.
        
        // Toms: map any GM tom variants to closest kit tom
        // (35-50 range toms are already handled above)
        
        // Cymbals: map exotic cymbals to closest kit cymbal
        { 54, 42 },  // Tambourine → Closed Hi-Hat
        { 56, 42 },  // Cowbell → Closed Hi-Hat
        
        // Percussion: map to closest drum
        // Notes below 35 or above 59 that appear in some MIDIs
        { 60, 48 },  // Hi Bongo → Rack Tom 1
        { 61, 45 },  // Low Bongo → Rack Tom 2
        { 62, 48 },  // Mute Hi Conga → Rack Tom 1
        { 63, 48 },  // Open Hi Conga → Rack Tom 1
        { 64, 43 },  // Low Conga → Floor Tom
        { 65, 48 },  // High Timbale → Rack Tom 1
        { 66, 45 },  // Low Timbale → Rack Tom 2
        { 67, 42 },  // High Agogo → Closed Hi-Hat
        { 68, 42 },  // Low Agogo → Closed Hi-Hat
        { 69, 42 },  // Cabasa → Closed Hi-Hat
        { 70, 42 },  // Maracas → Closed Hi-Hat
        { 71, 42 },  // Short Whistle → skip (will not map)
        { 72, 42 },  // Long Whistle → skip
        { 73, 42 },  // Short Guiro → Closed Hi-Hat
        { 74, 42 },  // Long Guiro → Closed Hi-Hat
        { 75, 37 },  // Claves → Side Stick (→ Snare)
        { 76, 48 },  // Hi Wood Block → Rack Tom 1
        { 77, 45 },  // Low Wood Block → Rack Tom 2
        { 78, 38 },  // Mute Cuica → Snare
        { 79, 38 },  // Open Cuica → Snare
        { 80, 42 },  // Mute Triangle → Closed Hi-Hat
        { 81, 46 },  // Open Triangle → Open Hi-Hat
    };

    /// <summary>
    /// Maps a GM note number to a note number that exists in the kit mapping.
    /// If the note is already mapped, returns it as-is. If it has a fallback, returns the fallback.
    /// If no mapping exists at all, returns the original note (GetLane will return null, and
    /// the note will be skipped by the transcription service).
    /// </summary>
    public static int MapGmNoteToKit(int gmNoteNumber)
    {
        // Already in our kit mapping? Return as-is.
        if (_noteToLane.ContainsKey(gmNoteNumber))
            return gmNoteNumber;

        // Has a fallback mapping? Return the fallback.
        if (_gmToKitFallback.TryGetValue(gmNoteNumber, out var fallback))
            return fallback;

        // No mapping at all — return original (will be skipped by transcription service)
        return gmNoteNumber;
    }

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
