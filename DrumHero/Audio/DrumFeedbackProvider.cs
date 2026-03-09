using DrumHero.Models;
using NAudio.Wave;

namespace DrumHero.Audio;

/// <summary>
/// An ISampleProvider that mixes triggered drum one-shots into the audio output.
/// Supports polyphony: multiple sounds can overlap (e.g., kick + hi-hat).
/// Velocity-sensitive: MIDI velocity scales the output amplitude.
///
/// Thread-safe for concurrent Trigger() calls from the MIDI input thread
/// while Read() is called from the audio thread.
/// </summary>
public class DrumFeedbackProvider : ISampleProvider
{
    /// <summary>
    /// Maximum number of simultaneously playing voices.
    /// Extra triggers beyond this silently replace the oldest voice.
    /// </summary>
    private const int MaxVoices = 16;

    private readonly Dictionary<DrumLane, float[]> _samples;
    private readonly Voice[] _voices;
    private readonly object _lock = new();
    private int _nextVoiceIndex;

    public WaveFormat WaveFormat { get; }

    public DrumFeedbackProvider(int sampleRate = 44100, int channels = 2)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        _samples = DrumSoundGenerator.GenerateAllSamples();
        _voices = new Voice[MaxVoices];
        for (int i = 0; i < MaxVoices; i++)
            _voices[i] = new Voice();
    }

    /// <summary>
    /// Triggers a drum sound for the given lane.
    /// Called from the MIDI input thread — must be fast and lock-free-ish.
    /// </summary>
    /// <param name="lane">Which drum was hit</param>
    /// <param name="velocity">MIDI velocity 1-127</param>
    public void Trigger(DrumLane lane, int velocity)
    {
        if (!_samples.TryGetValue(lane, out var sampleData))
            return;

        // Normalize velocity to 0.3–1.0 range (never fully silent for a hit)
        float gain = Math.Clamp(velocity / 127f, 0.3f, 1.0f);

        lock (_lock)
        {
            // Use round-robin voice allocation (oldest voice gets stolen)
            var voice = _voices[_nextVoiceIndex];
            voice.SampleData = sampleData;
            voice.Position = 0;
            voice.Gain = gain;
            voice.IsActive = true;

            _nextVoiceIndex = (_nextVoiceIndex + 1) % MaxVoices;
        }
    }

    /// <summary>
    /// Called by the mixer on the audio thread. Sums all active voices into the buffer.
    /// </summary>
    public int Read(float[] buffer, int offset, int count)
    {
        // Zero the output region first
        Array.Clear(buffer, offset, count);

        lock (_lock)
        {
            for (int v = 0; v < MaxVoices; v++)
            {
                var voice = _voices[v];
                if (!voice.IsActive || voice.SampleData == null)
                    continue;

                int remaining = voice.SampleData.Length - voice.Position;
                int toWrite = Math.Min(remaining, count);

                for (int i = 0; i < toWrite; i++)
                {
                    buffer[offset + i] += voice.SampleData[voice.Position + i] * voice.Gain;
                }

                voice.Position += toWrite;
                if (voice.Position >= voice.SampleData.Length)
                {
                    voice.IsActive = false;
                }
            }
        }

        return count;
    }

    private class Voice
    {
        public float[]? SampleData;
        public int Position;
        public float Gain = 1.0f;
        public bool IsActive;
    }
}
