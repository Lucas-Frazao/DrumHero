using DrumHero.Models;
using NAudio.Midi;
using System.IO;
using System.Text.Json;

namespace DrumHero.Analysis;

/// <summary>
/// Parses a standard MIDI file (e.g. from Songsterr) and produces a transcription.json
/// compatible with DrumHero's highway renderer.
/// 
/// Reads all NoteOn events on MIDI Channel 10 (percussion), converts tick-based timing
/// to seconds using the file's tempo map, and maps General MIDI drum note numbers to
/// the user's Alesis Nitro Max kit lanes via MidiDrumMap.
/// </summary>
public class MidiTranscriptionService
{
    /// <summary>
    /// Maximum number of drum notes that can appear at the same time on the highway.
    /// When a MIDI file has more simultaneous notes, the lowest-priority ones are dropped.
    /// </summary>
    private const int MaxSimultaneousNotes = 3;

    /// <summary>
    /// Two notes are considered "simultaneous" if they are within this time window.
    /// Accounts for slight MIDI quantization differences.
    /// </summary>
    private const double SimultaneousThresholdSeconds = 0.005; // 5ms

    /// <summary>
    /// Parses a MIDI file and writes the transcription JSON to the specified output path.
    /// </summary>
    /// <param name="midiFilePath">Path to the .mid file (e.g. downloaded from Songsterr)</param>
    /// <param name="outputJsonPath">Path where transcription.json will be written</param>
    /// <param name="songDurationSeconds">Duration of the audio file in seconds (for metadata)</param>
    /// <param name="progress">Optional progress reporter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task GenerateTranscriptionAsync(
        string midiFilePath,
        string outputJsonPath,
        double songDurationSeconds,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report(new AnalysisProgress
        {
            Stage = "Transcribing",
            ProgressFraction = 0.75,
            Message = "Parsing drum MIDI file..."
        });

        var midiFile = new MidiFile(midiFilePath, strictChecking: false);

        // Build tempo map from all tracks (tempo events can be on any track, typically track 0)
        var tempoMap = BuildTempoMap(midiFile);
        var ticksPerQuarterNote = midiFile.DeltaTicksPerQuarterNote;

        // Extract time signature (use first one found, default to 4/4)
        var (tsNumerator, tsDenominator) = ExtractTimeSignature(midiFile);

        // Find and extract drum track (Channel 10 = channel index 10 in NAudio, 1-based)
        var rawNotes = new List<RawMidiNote>();

        for (int trackIndex = 0; trackIndex < midiFile.Tracks; trackIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var midiEvent in midiFile.Events[trackIndex])
            {
                // NAudio already provides AbsoluteTime (cumulative ticks from file start)
                // Check for NoteOn events on channel 10 (NAudio uses 1-based channel numbers)
                if (midiEvent is NoteOnEvent noteOn &&
                    noteOn.Channel == 10 &&
                    noteOn.Velocity > 0)
                {
                    rawNotes.Add(new RawMidiNote
                    {
                        AbsoluteTick = noteOn.AbsoluteTime,
                        NoteNumber = noteOn.NoteNumber,
                        Velocity = noteOn.Velocity
                    });
                }
            }
        }

        progress?.Report(new AnalysisProgress
        {
            Stage = "Transcribing",
            ProgressFraction = 0.85,
            Message = $"Found {rawNotes.Count} drum notes, mapping to kit..."
        });

        // Detect BPM from tempo map (use the first tempo, or 120 if none)
        double bpm = GetPrimaryBpm(tempoMap);

        // Convert ticks to seconds and map to kit
        var notes = new List<TranscriptionNote>();
        double secondsPerBeat = 60.0 / bpm;

        foreach (var raw in rawNotes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double timeSeconds = TickToSeconds(raw.AbsoluteTick, tempoMap, ticksPerQuarterNote);

            // Map GM note number to our kit lane
            int mappedNote = MidiDrumMap.MapGmNoteToKit(raw.NoteNumber);

            // Get the drum type name for the transcription
            var lane = MidiDrumMap.GetLane(mappedNote);
            if (lane == null) continue; // Skip unmappable notes (e.g. bongos, timbales)

            string drumType = GetDrumTypeName(lane.Value);

            // Calculate bar/beat/sub-beat position
            double beatPosition = timeSeconds / secondsPerBeat;
            int bar = (int)(beatPosition / tsNumerator) + 1;
            int beat = (int)(beatPosition % tsNumerator) + 1;
            int subBeat = (int)((beatPosition % 1) * 8) + 1;

            // Normalize velocity to 0.0 - 1.0
            double velocity = Math.Clamp(raw.Velocity / 127.0, 0.1, 1.0);

            notes.Add(new TranscriptionNote
            {
                TimeSeconds = Math.Round(timeSeconds, 6),
                MidiNote = mappedNote,
                Velocity = Math.Round(velocity, 3),
                Bar = bar,
                Beat = beat,
                SubBeat = subBeat,
                DrumType = drumType
            });
        }

        // Sort by time
        notes.Sort((a, b) => a.TimeSeconds.CompareTo(b.TimeSeconds));

        // Limit simultaneous notes to MaxSimultaneousNotes (3).
        // When more than 3 notes land at the same time, keep the highest-priority
        // drums based on a fixed hierarchy: kick > snare > hi-hat > toms > cymbals > ride.
        notes = LimitSimultaneousNotes(notes, MaxSimultaneousNotes);

        // Build transcription data
        double midiDuration = notes.Count > 0 ? notes[^1].TimeSeconds + 1.0 : 0;
        double duration = songDurationSeconds > 0 ? songDurationSeconds : midiDuration;

        var transcription = new TranscriptionData
        {
            BPM = Math.Round(bpm, 2),
            TimeSignature = new TimeSignatureInfo
            {
                Numerator = tsNumerator,
                Denominator = tsDenominator
            },
            DurationSeconds = Math.Round(duration, 3),
            Notes = notes
        };

        var json = JsonSerializer.Serialize(transcription, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputJsonPath, json, cancellationToken);

        progress?.Report(new AnalysisProgress
        {
            Stage = "Transcribing",
            ProgressFraction = 0.95,
            Message = $"Transcription complete: {notes.Count} notes at {bpm:F0} BPM"
        });
    }

    #region Tempo Map

    /// <summary>
    /// Internal representation of a tempo change point in the MIDI file.
    /// Named to avoid collision with NAudio.Midi.TempoEvent.
    /// </summary>
    private class TempoMapEntry
    {
        public long Tick { get; set; }
        public double MicrosecondsPerQuarterNote { get; set; }
        public double Bpm => 60_000_000.0 / MicrosecondsPerQuarterNote;
    }

    /// <summary>
    /// Builds a sorted list of tempo changes from the MIDI file.
    /// </summary>
    private static List<TempoMapEntry> BuildTempoMap(MidiFile midiFile)
    {
        var entries = new List<TempoMapEntry>();

        for (int trackIndex = 0; trackIndex < midiFile.Tracks; trackIndex++)
        {
            foreach (var midiEvent in midiFile.Events[trackIndex])
            {
                // NAudio's TempoEvent has AbsoluteTime and MicrosecondsPerQuarterNote
                if (midiEvent is NAudio.Midi.TempoEvent tempoEvt)
                {
                    entries.Add(new TempoMapEntry
                    {
                        Tick = tempoEvt.AbsoluteTime,
                        MicrosecondsPerQuarterNote = tempoEvt.MicrosecondsPerQuarterNote
                    });
                }
            }
        }

        // Sort by tick position
        entries.Sort((a, b) => a.Tick.CompareTo(b.Tick));

        // If no tempo events found, default to 120 BPM (500,000 µs/quarter note)
        if (entries.Count == 0)
        {
            entries.Add(new TempoMapEntry
            {
                Tick = 0,
                MicrosecondsPerQuarterNote = 500_000
            });
        }

        return entries;
    }

    /// <summary>
    /// Converts an absolute tick position to seconds using the tempo map.
    /// Handles tempo changes correctly by accumulating time across segments.
    /// </summary>
    private static double TickToSeconds(long tick, List<TempoMapEntry> tempoMap, int ticksPerQuarterNote)
    {
        double seconds = 0;
        long lastTick = 0;
        double currentMicrosecondsPerTick = tempoMap[0].MicrosecondsPerQuarterNote / ticksPerQuarterNote;

        for (int i = 1; i < tempoMap.Count; i++)
        {
            if (tempoMap[i].Tick >= tick) break;

            // Accumulate time from lastTick to this tempo change
            long deltaTicks = tempoMap[i].Tick - lastTick;
            seconds += deltaTicks * currentMicrosecondsPerTick / 1_000_000.0;

            lastTick = tempoMap[i].Tick;
            currentMicrosecondsPerTick = tempoMap[i].MicrosecondsPerQuarterNote / ticksPerQuarterNote;
        }

        // Add remaining ticks from last tempo change to target tick
        long remainingTicks = tick - lastTick;
        seconds += remainingTicks * currentMicrosecondsPerTick / 1_000_000.0;

        return seconds;
    }

    /// <summary>
    /// Returns the primary BPM (first tempo event, or 120 if none).
    /// </summary>
    private static double GetPrimaryBpm(List<TempoMapEntry> tempoMap)
    {
        return tempoMap.Count > 0 ? tempoMap[0].Bpm : 120.0;
    }

    #endregion

    #region Time Signature

    private static (int numerator, int denominator) ExtractTimeSignature(MidiFile midiFile)
    {
        for (int trackIndex = 0; trackIndex < midiFile.Tracks; trackIndex++)
        {
            foreach (var midiEvent in midiFile.Events[trackIndex])
            {
                if (midiEvent is TimeSignatureEvent tsEvent)
                {
                    // NAudio TimeSignatureEvent: Numerator is direct, Denominator is power of 2
                    int denominator = (int)Math.Pow(2, tsEvent.Denominator);
                    return (tsEvent.Numerator, denominator);
                }
            }
        }

        return (4, 4); // Default
    }

    #endregion

    #region Simultaneous Note Limiting

    /// <summary>
    /// Priority order for drum types when limiting simultaneous notes.
    /// Lower value = higher priority = more likely to be kept.
    /// Hierarchy: Kick > Snare > Hi-Hat > Toms > Cymbals > Ride
    /// </summary>
    private static int GetDrumPriority(int midiNote)
    {
        var lane = MidiDrumMap.GetLane(midiNote);
        return lane switch
        {
            DrumLane.LeftKick or DrumLane.RightKick => 0,   // Kick is the backbone
            DrumLane.Snare => 1,                             // Snare is the backbeat
            DrumLane.ClosedHiHat or DrumLane.OpenHiHat => 2, // Hi-hat drives the groove
            DrumLane.FloorTom => 3,                          // Floor tom (fills, accents)
            DrumLane.RackTom1 => 4,                          // Rack tom 1
            DrumLane.RackTom2 => 5,                          // Rack tom 2
            DrumLane.Crash1 => 6,                            // Crash 1 (accent cymbal)
            DrumLane.Crash2 => 7,                            // Crash 2
            DrumLane.Crash3 => 8,                            // Crash 3
            DrumLane.Ride => 9,                              // Ride (often doubles with hi-hat)
            _ => 10
        };
    }

    /// <summary>
    /// Filters a sorted list of transcription notes so that no more than <paramref name="maxNotes"/>
    /// appear at the same time. Notes within <see cref="SimultaneousThresholdSeconds"/> of each other
    /// are considered simultaneous. The highest-priority notes (by drum type) are kept.
    /// </summary>
    private static List<TranscriptionNote> LimitSimultaneousNotes(
        List<TranscriptionNote> sortedNotes, int maxNotes)
    {
        if (sortedNotes.Count == 0) return sortedNotes;

        var result = new List<TranscriptionNote>(sortedNotes.Count);
        var group = new List<TranscriptionNote> { sortedNotes[0] };

        for (int i = 1; i < sortedNotes.Count; i++)
        {
            // Check if this note is simultaneous with the current group
            if (sortedNotes[i].TimeSeconds - group[0].TimeSeconds <= SimultaneousThresholdSeconds)
            {
                group.Add(sortedNotes[i]);
            }
            else
            {
                // Flush the previous group
                FlushGroup(group, maxNotes, result);
                group.Clear();
                group.Add(sortedNotes[i]);
            }
        }

        // Flush the last group
        FlushGroup(group, maxNotes, result);

        return result;
    }

    private static void FlushGroup(
        List<TranscriptionNote> group, int maxNotes, List<TranscriptionNote> output)
    {
        if (group.Count <= maxNotes)
        {
            output.AddRange(group);
        }
        else
        {
            // Sort by priority (lowest = highest priority), take the top N
            var prioritized = group
                .OrderBy(n => GetDrumPriority(n.MidiNote))
                .Take(maxNotes);
            output.AddRange(prioritized);
        }
    }

    #endregion

    #region Drum Type Naming

    private static string GetDrumTypeName(DrumLane lane) => lane switch
    {
        DrumLane.LeftKick or DrumLane.RightKick => "kick",
        DrumLane.Snare => "snare",
        DrumLane.RackTom1 => "rack_tom_1",
        DrumLane.RackTom2 => "rack_tom_2",
        DrumLane.FloorTom => "floor_tom",
        DrumLane.ClosedHiHat => "hihat_closed",
        DrumLane.OpenHiHat => "hihat_open",
        DrumLane.Crash1 => "crash_1",
        DrumLane.Crash2 => "crash_2",
        DrumLane.Crash3 => "crash_3",
        DrumLane.Ride => "ride",
        _ => "unknown"
    };

    #endregion

    /// <summary>
    /// Internal representation of a raw MIDI note before mapping.
    /// </summary>
    private class RawMidiNote
    {
        public long AbsoluteTick { get; set; }
        public int NoteNumber { get; set; }
        public int Velocity { get; set; }
    }
}
