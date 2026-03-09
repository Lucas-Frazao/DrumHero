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

        var midiFile = new MidiFile(midiFilePath, strictMode: false);

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
