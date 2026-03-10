using DrumHero.Models;

namespace DrumHero.Audio;

/// <summary>
/// Generates synthesized drum one-shot samples modeled after Lars Ulrich's
/// drum sound on Metallica's "…And Justice for All" (1988).
///
/// Key sonic characteristics targeted:
///   - Kick: massive, bold bass-dominant thump with deep sub-bass (40-55 Hz),
///     layered harmonics, heavy saturation, long sustain. Minimal beater click.
///   - Snare: thin, bright, papery crack with prominent snare wire buzz,
///     minimal body, fast gated decay.
///   - Toms: deep-tuned Tama shells (13"/15" rack, 18-20" floor), heavy
///     pitch drop on attack, gated sustain, dry and punchy.
///   - Hi-hats: bright, cutting, tight.
///   - Cymbals: bright and aggressive, relatively dry sustain.
///   - Overall: dry, sterile, no room ambience, treble-forward.
///
/// All samples are pre-computed at construction for zero-latency playback.
/// Stereo interleaved float arrays (L, R, L, R, …).
/// </summary>
public static class DrumSoundGenerator
{
    private const int SampleRate = 44100;

    public static Dictionary<DrumLane, float[]> GenerateAllSamples()
    {
        return new Dictionary<DrumLane, float[]>
        {
            { DrumLane.LeftKick, GenerateKick() },
            { DrumLane.RightKick, GenerateKick() },
            { DrumLane.Snare, GenerateSnare() },
            { DrumLane.ClosedHiHat, GenerateClosedHiHat() },
            { DrumLane.OpenHiHat, GenerateOpenHiHat() },
            { DrumLane.RackTom1, GenerateTom(170, 0.22) },   // 13" rack tom — high, tight
            { DrumLane.RackTom2, GenerateTom(120, 0.26) },   // 15" rack tom — mid, gated
            { DrumLane.FloorTom, GenerateTom(75, 0.32) },    // 18-20" floor tom — deep, boomy
            { DrumLane.Crash1, GenerateCrash(1.0, 4800) },    // Crash 1 — bright, splashy
            { DrumLane.Crash2, GenerateCrash(0.90, 4400) },    // Crash 2 — slightly darker
            { DrumLane.Crash3, GenerateCrash(0.80, 5200) },    // Crash 3 / China — cutting
            { DrumLane.Ride, GenerateRide() },
        };
    }

    /// <summary>
    /// Kick drum: massive, bold bass that dominates the mix.
    /// Very deep sub-bass (40-55 Hz), long sustain, layered harmonics,
    /// heavy saturation for loudness. Beater click kept minimal.
    /// </summary>
    private static float[] GenerateKick()
    {
        double duration = 0.50; // long sustain for maximum bass presence
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2];
        var rng = new Random(88);

        double phase = 0;
        double phase2 = 0;
        double phase3 = 0;
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            // Deep bass body: starts at ~75 Hz, drops to ~40 Hz
            double freq = 40 + 35 * Math.Exp(-t * 18);

            // Slow decay — bass hangs and fills the room
            double bodyEnv = Math.Exp(-t * 5.0);

            phase += 2 * Math.PI * freq / SampleRate;
            double body = Math.Sin(phase) * bodyEnv * 1.0;

            // Sub-bass layer (one octave below) — chest-punch rumble
            double subFreq = freq * 0.5;
            phase2 += 2 * Math.PI * subFreq / SampleRate;
            double sub = Math.Sin(phase2) * Math.Exp(-t * 6.0) * 0.6;

            // Second harmonic (one octave above) — adds perceived loudness
            phase3 += 2 * Math.PI * (freq * 2) / SampleRate;
            double harmonic = Math.Sin(phase3) * Math.Exp(-t * 12.0) * 0.25;

            // Subtle beater click — just enough attack definition
            double click = 0;
            if (t < 0.008)
            {
                double clickEnv = Math.Exp(-t * 450);
                click += Math.Sin(2 * Math.PI * 3000 * t) * 0.15 * clickEnv;
                click += (rng.NextDouble() * 2 - 1) * 0.08 * Math.Exp(-t * 600);
            }

            double sample = body + sub + harmonic + click;

            // Heavy saturation — drives loudness and adds grit/warmth
            sample = Math.Tanh(sample * 1.8);

            // Final output gain boost
            float s = (float)(sample * 1.4);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }

    /// <summary>
    /// AJFA Snare: thin, dry, bright, papery crack. Prominent snare wire
    /// buzz in the upper mids/highs, minimal shell body resonance.
    /// Fast gated decay. Very up-front and aggressive.
    /// </summary>
    private static float[] GenerateSnare()
    {
        double duration = 0.18; // short, gated
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2];
        var rng = new Random(42);

        double phase = 0;
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            // Shell tone: thin, high-tuned ~240 Hz, decays FAST
            double toneFreq = 240 + 40 * Math.Exp(-t * 50);
            phase += 2 * Math.PI * toneFreq / SampleRate;
            double tone = Math.Sin(phase) * Math.Exp(-t * 25) * 0.30;

            // Snare wire buzz: prominent bright noise, the defining character
            // Decays a bit slower than the shell tone — that papery sizzle
            double wireNoise = (rng.NextDouble() * 2 - 1);
            // High-pass character: emphasize upper frequencies
            double wireEnv = Math.Exp(-t * 14);
            double wires = wireNoise * wireEnv * 0.55;

            // Hard transient crack at onset
            double crack = 0;
            if (t < 0.003)
            {
                crack = (rng.NextDouble() * 2 - 1) * 0.75 * Math.Exp(-t * 1200);
                // High-frequency stick impact
                crack += Math.Sin(2 * Math.PI * 4500 * t) * 0.4 * Math.Exp(-t * 800);
            }

            // Minimal low-end body (thin, not warm)
            double sample = tone + wires + crack;
            sample = Math.Tanh(sample * 1.3);

            float s = (float)(sample * 1.0);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }

    /// <summary>
    /// AJFA Closed hi-hat: bright, tight, cutting. Crisp attack.
    /// </summary>
    private static float[] GenerateClosedHiHat()
    {
        double duration = 0.06; // very tight
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2];
        var rng = new Random(123);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            double noise = rng.NextDouble() * 2 - 1;

            // Very fast, crisp decay
            double env = Math.Exp(-t * 80);

            // Bright metallic ring — pushed high for that cutting AJFA cymbal tone
            double ring1 = Math.Sin(2 * Math.PI * 7500 * t) * 0.18 * Math.Exp(-t * 100);
            double ring2 = Math.Sin(2 * Math.PI * 9500 * t) * 0.10 * Math.Exp(-t * 110);

            // Stick contact transient
            double stick = t < 0.002 ? (rng.NextDouble() * 2 - 1) * 0.5 * Math.Exp(-t * 1500) : 0;

            double sample = noise * env * 0.5 + ring1 + ring2 + stick;

            float s = (float)(sample * 0.85);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }

    /// <summary>
    /// AJFA Open hi-hat: bright and sizzly but controlled sustain. Dry.
    /// </summary>
    private static float[] GenerateOpenHiHat()
    {
        double duration = 0.35; // shorter than generic — dry, controlled
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2];
        var rng = new Random(456);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            double noise = rng.NextDouble() * 2 - 1;
            double env = Math.Exp(-t * 8); // moderately fast decay

            // Bright, aggressive metallic partials
            double ring1 = Math.Sin(2 * Math.PI * 6500 * t) * 0.14;
            double ring2 = Math.Sin(2 * Math.PI * 8500 * t) * 0.10;
            double ring3 = Math.Sin(2 * Math.PI * 11000 * t) * 0.05;

            double sample = (noise * 0.40 + ring1 + ring2 + ring3) * env;

            float s = (float)(sample * 0.80);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }

    /// <summary>
    /// AJFA Tom: deep-tuned Tama shells, heavy pitch drop on attack,
    /// gated sustain (very dry, damped). Punchy transient, low resonance.
    /// Lars used 13"/15" rack toms and 18-20" floor toms, tuned low.
    /// </summary>
    /// <param name="baseFreq">Fundamental frequency (lower = deeper tom)</param>
    /// <param name="duration">Sustain duration — kept short for gated feel</param>
    private static float[] GenerateTom(double baseFreq, double duration)
    {
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2];
        var rng = new Random((int)(baseFreq * 7)); // deterministic per tom

        double phase = 0;
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            // Heavy pitch drop on attack — characteristic of deep-tuned toms
            double freq = baseFreq + 100 * Math.Exp(-t * 35);

            phase += 2 * Math.PI * freq / SampleRate;
            double tone = Math.Sin(phase);

            // Gated envelope: fast attack, then rapid cutoff (damped heads, no ring)
            // Two-stage: initial punch then steep gate
            double env = Math.Exp(-t * 12) * 0.7 + Math.Exp(-t * 30) * 0.3;

            // Stick attack transient — punchy
            double attack = 0;
            if (t < 0.005)
            {
                attack = Math.Exp(-t * 600) * 0.4;
                attack += (rng.NextDouble() * 2 - 1) * 0.15 * Math.Exp(-t * 800);
            }

            // Slight skin slap noise in upper mids
            double skinNoise = (rng.NextDouble() * 2 - 1) * Math.Exp(-t * 40) * 0.08;

            double sample = Math.Tanh((tone * env * 0.75 + attack + skinNoise) * 1.2);

            float s = (float)(sample * 0.95);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }

    /// <summary>
    /// AJFA Crash cymbal: bright, aggressive, cutting attack with splashy sustain.
    /// More wash and shimmer than a dry crash — the initial burst explodes and the
    /// cymbal sustains with layered metallic overtones and noise wash.
    /// </summary>
    /// <param name="duration">Sustain length in seconds</param>
    /// <param name="baseRing">Base frequency for metallic ring partials</param>
    private static float[] GenerateCrash(double duration, double baseRing)
    {
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2];
        var rng = new Random((int)(baseRing));

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            // Initial explosive hit — loud, bright burst
            double noise = rng.NextDouble() * 2 - 1;
            double burstEnv = Math.Exp(-t * 18) * 0.50;

            // Splashy sustain wash — slower decay for that open, washy sustain
            double washEnv = Math.Exp(-t * 1.8);

            // Bright, aggressive metallic partials — more layers for shimmer
            double ring1 = Math.Sin(2 * Math.PI * baseRing * t) * 0.14;
            double ring2 = Math.Sin(2 * Math.PI * (baseRing * 1.31) * t) * 0.11;
            double ring3 = Math.Sin(2 * Math.PI * (baseRing * 1.73) * t) * 0.08;
            double ring4 = Math.Sin(2 * Math.PI * (baseRing * 2.19) * t) * 0.06;
            double ring5 = Math.Sin(2 * Math.PI * (baseRing * 2.87) * t) * 0.04;
            double ring6 = Math.Sin(2 * Math.PI * (baseRing * 3.51) * t) * 0.025;
            double ringEnv = Math.Exp(-t * 2.0);

            // High-frequency sizzle that sustains — the "splash" character
            double sizzle = (rng.NextDouble() * 2 - 1) * 0.12 * Math.Exp(-t * 2.5);

            double sample = noise * burstEnv
                          + noise * washEnv * 0.38
                          + (ring1 + ring2 + ring3 + ring4 + ring5 + ring6) * ringEnv
                          + sizzle;

            float s = (float)(sample * 0.78);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }

    /// <summary>
    /// AJFA Ride: tight bell ping with bright stick definition.
    /// Controlled wash, not too much sustain. Dry and precise.
    /// </summary>
    private static float[] GenerateRide()
    {
        double duration = 0.50; // shorter than typical — dry
        int samples = (int)(SampleRate * duration);
        var buffer = new float[samples * 2];
        var rng = new Random(321);

        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / SampleRate;

            // Prominent bell ping — the stick-on-metal sound
            double bell = Math.Sin(2 * Math.PI * 3200 * t) * 0.28 * Math.Exp(-t * 5);

            // Bright stick click transient
            double ping = Math.Sin(2 * Math.PI * 4200 * t) * Math.Exp(-t * 35) * 0.25;

            // Minimal wash — dry ride, not crashy
            double noise = (rng.NextDouble() * 2 - 1) * 0.10 * Math.Exp(-t * 7);

            // Upper harmonic shimmer
            double shimmer = Math.Sin(2 * Math.PI * 6800 * t) * 0.05 * Math.Exp(-t * 8);

            double sample = bell + ping + noise + shimmer;

            float s = (float)(sample * 0.80);
            buffer[i * 2] = s;
            buffer[i * 2 + 1] = s;
        }

        return buffer;
    }
}
