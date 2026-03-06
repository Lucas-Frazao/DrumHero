using System.Text.Json.Serialization;

namespace DrumHero.Models;

/// <summary>
/// Root object for the drum transcription JSON returned by the analysis service.
/// </summary>
public class TranscriptionData
{
    [JsonPropertyName("bpm")]
    public double BPM { get; set; }
    
    [JsonPropertyName("time_signature")]
    public TimeSignatureInfo TimeSignature { get; set; } = new();
    
    [JsonPropertyName("duration_seconds")]
    public double DurationSeconds { get; set; }
    
    [JsonPropertyName("notes")]
    public List<TranscriptionNote> Notes { get; set; } = new();
}

public class TimeSignatureInfo
{
    [JsonPropertyName("numerator")]
    public int Numerator { get; set; } = 4;
    
    [JsonPropertyName("denominator")]
    public int Denominator { get; set; } = 4;
}

public class TranscriptionNote
{
    [JsonPropertyName("time_seconds")]
    public double TimeSeconds { get; set; }
    
    [JsonPropertyName("midi_note")]
    public int MidiNote { get; set; }
    
    [JsonPropertyName("velocity")]
    public double Velocity { get; set; } = 0.8;
    
    [JsonPropertyName("bar")]
    public int Bar { get; set; }
    
    [JsonPropertyName("beat")]
    public int Beat { get; set; }
    
    [JsonPropertyName("sub_beat")]
    public int SubBeat { get; set; }
    
    [JsonPropertyName("drum_type")]
    public string DrumType { get; set; } = string.Empty;  // e.g. "kick", "snare", "hihat_closed"
}
