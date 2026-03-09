using DrumHero.Models;

namespace DrumHero.Audio;

/// <summary>
/// Generates synthesized drum one-shot samples in memory.
/// Each drum sound is modeled with sine/noise components, pitch envelopes,
/// and amplitude envelopes to approximate real drum characteristics.
/// All samples are pre-computed at construction for zero-latency playback.
/// </summary>
public static class DrumSoundGenerator
{
    private const int SampleRate = 44100;

    /// <summary>
    /// Pre-generates all drum samples, keyed by DrumLane.
    /// Returns stereo interleaved float arrays (L, R, L, R, ...).
    /// </summary>
    public static Dictionary<DrumLane, float[]> GenerateAllSamples()
    {
        return new Dictionary<DrumLane, float[]>
        {
            { DrumLane.LeftKick, GenerateKick() },
            { DrumLane.RightKick, GenerateKick() },
            { DrumLane.Snare, GenerateSnare() },
            { DrumLane.ClosedHiHat, GenerateClosedHiHat() },
            { DrumLane.OpenHiHat, GenerateOpenHiHat() },
            { DrumLane.RackTom1, GenerateTom(200, 0.30) },   // High tom
            { DrumLane.RackTom2, GenerateTom(150, 0.35) },   // Mid tom
            { DrumLane.FloorTom, GenerateTom(100, 0.40) },   // Floor tom
            { DrumLane.Crash1, GenerateCrash(0.55) },
            { DrumLane.Crash2, GenerateCrash(0.50) },
            { DrumLane.Crash3, GenerateCrash(0.45) },
            { DrumLane.Ride, GenerateRide() },
        };
    }

    /// <summary>
    /// Kick drum: low sine wave with sharp pitch drop + sub-bass body.
    /// </summary>
    private static float[] GenerateKick()
    {
        double duration = 0.35;
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2]; // stereo

        double phase = 0;
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            // Pitch envelope: starts at ~150 Hz, drops to ~50 Hz
            double freq = 50 + 100 * Math.Exp(-t * 25);

            // Amplitude envelope: sharp attack, medium decay
            double ampEnv = Math.Exp(-t * 8.0);

            // Transient click at the very start
            double click = t < 0.005 ? Math.Exp(-t * 800) * 0.4 : 0;

            phase += 2 * Math.PI * freq / SampleRate;
            double sample = Math.Sin(phase) * ampEnv * 0.85 + click;

            // Soft clip for warmth
            sample = Math.Tanh(sample * 1.2);

            float s = (float)(sample * 0.9);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }

    /// <summary>
    /// Snare drum: mid-frequency tone body + filtered noise for snare wires.
    /// </summary>
    private static float[] GenerateSnare()
    {
        double duration = 0.25;
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2];
        var rng = new Random(42); // deterministic for consistent sound

        double phase = 0;
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            // Tone body: ~200 Hz with fast decay
            double toneFreq = 200 + 50 * Math.Exp(-t * 30);
            phase += 2 * Math.PI * toneFreq / SampleRate;
            double tone = Math.Sin(phase) * Math.Exp(-t * 15) * 0.5;

            // Snare noise: white noise with bandpass character, longer tail
            double noise = (rng.NextDouble() * 2 - 1) * Math.Exp(-t * 10) * 0.55;

            // Transient crack
            double crack = t < 0.003 ? (rng.NextDouble() * 2 - 1) * 0.6 : 0;

            double sample = Math.Tanh((tone + noise + crack) * 1.1);

            float s = (float)(sample * 0.85);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }

    /// <summary>
    /// Closed hi-hat: high-frequency filtered noise, very short decay.
    /// </summary>
    private static float[] GenerateClosedHiHat()
    {
        double duration = 0.08;
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2];
        var rng = new Random(123);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            // High-frequency metallic noise
            double noise = rng.NextDouble() * 2 - 1;

            // Shape with very fast decay
            double env = Math.Exp(-t * 60);

            // Add some high-freq ring
            double ring = Math.Sin(2 * Math.PI * 6000 * t) * 0.15 * Math.Exp(-t * 80);

            double sample = (noise * env * 0.6 + ring);

            float s = (float)(sample * 0.7);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }

    /// <summary>
    /// Open hi-hat: similar to closed but much longer decay, slight tonal ring.
    /// </summary>
    private static float[] GenerateOpenHiHat()
    {
        double duration = 0.45;
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2];
        var rng = new Random(456);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            double noise = rng.NextDouble() * 2 - 1;
            double env = Math.Exp(-t * 6); // slow decay

            // Metallic ring components
            double ring1 = Math.Sin(2 * Math.PI * 5500 * t) * 0.12;
            double ring2 = Math.Sin(2 * Math.PI * 7200 * t) * 0.08;

            double sample = (noise * 0.5 + ring1 + ring2) * env;

            float s = (float)(sample * 0.65);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }

    /// <summary>
    /// Tom drum: sine tone with pitch drop, resonant body.
    /// </summary>
    /// <param name="baseFreq">Fundamental frequency (higher = higher tom)</param>
    /// <param name="duration">Sustain duration in seconds</param>
    private static float[] GenerateTom(double baseFreq, double duration)
    {
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2];

        double phase = 0;
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            // Pitch drops slightly on attack
            double freq = baseFreq + 60 * Math.Exp(-t * 20);

            phase += 2 * Math.PI * freq / SampleRate;
            double tone = Math.Sin(phase);

            // Amplitude: fast attack, medium-slow decay
            double env = Math.Exp(-t * 7);

            // Slight click transient
            double click = t < 0.004 ? Math.Exp(-t * 500) * 0.3 : 0;

            double sample = Math.Tanh((tone * env * 0.7 + click) * 1.1);

            float s = (float)(sample * 0.8);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }

    /// <summary>
    /// Crash cymbal: layered noise bands with long decay and metallic ring.
    /// </summary>
    private static float[] GenerateCrash(double duration)
    {
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2];
        var rng = new Random(789);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            // Noise burst for initial impact
            double noise = (rng.NextDouble() * 2 - 1);
            double noiseEnv = Math.Exp(-t * 3.5);

            // Metallic shimmer: multiple detuned high partials
            double ring1 = Math.Sin(2 * Math.PI * 4200 * t) * 0.10;
            double ring2 = Math.Sin(2 * Math.PI * 5800 * t) * 0.08;
            double ring3 = Math.Sin(2 * Math.PI * 7400 * t) * 0.05;
            double ringEnv = Math.Exp(-t * 2.5);

            // Initial burst is louder
            double burstEnv = Math.Exp(-t * 20) * 0.3;

            double sample = noise * noiseEnv * 0.4
                          + (ring1 + ring2 + ring3) * ringEnv
                          + noise * burstEnv;

            float s = (float)(sample * 0.6);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }

    /// <summary>
    /// Ride cymbal: tighter than crash, prominent bell tone, controlled sustain.
    /// </summary>
    private static float[] GenerateRide()
    {
        double duration = 0.6;
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2];
        var rng = new Random(321);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            // Bell tone: prominent mid-high frequency
            double bell = Math.Sin(2 * Math.PI * 3000 * t) * 0.25 * Math.Exp(-t * 4);

            // Subtle wash
            double noise = (rng.NextDouble() * 2 - 1) * 0.15 * Math.Exp(-t * 5);

            // Metallic partials
            double ring = Math.Sin(2 * Math.PI * 5000 * t) * 0.06 * Math.Exp(-t * 6);

            // Ping transient
            double ping = Math.Sin(2 * Math.PI * 3500 * t) * Math.Exp(-t * 30) * 0.2;

            double sample = bell + noise + ring + ping;

            float s = (float)(sample * 0.65);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }
}
