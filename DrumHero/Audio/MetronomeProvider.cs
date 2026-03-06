using NAudio.Wave;

namespace DrumHero.Audio;

/// <summary>
/// Generates metronome click audio samples at a given BPM.
/// Accent on beat 1, regular click on other beats.
/// </summary>
public class MetronomeProvider : ISampleProvider
{
    private readonly double _bpm;
    private readonly int _beatsPerBar;
    private readonly int _sampleRate;
    private long _samplePosition;
    private int _currentBeat;
    
    // Pre-generated click sounds
    private readonly float[] _accentClick;
    private readonly float[] _normalClick;
    
    public WaveFormat WaveFormat { get; }
    
    public MetronomeProvider(double bpm, int beatsPerBar, int sampleRate)
    {
        _bpm = bpm;
        _beatsPerBar = beatsPerBar;
        _sampleRate = sampleRate;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        
        _accentClick = GenerateClick(1000, 0.015, 0.8); // Higher pitch, louder
        _normalClick = GenerateClick(800, 0.012, 0.5);  // Lower pitch, softer
    }
    
    public int Read(float[] buffer, int offset, int count)
    {
        var samplesPerBeat = (int)(_sampleRate * 60.0 / _bpm);
        
        for (int i = 0; i < count; i += 2) // stereo
        {
            var posInBeat = (int)(_samplePosition % samplesPerBeat);
            var beatNumber = (int)((_samplePosition / samplesPerBeat) % _beatsPerBar);
            
            float sample = 0;
            
            var click = beatNumber == 0 ? _accentClick : _normalClick;
            if (posInBeat < click.Length)
            {
                sample = click[posInBeat];
            }
            
            buffer[offset + i] = sample;       // Left
            buffer[offset + i + 1] = sample;   // Right
            _samplePosition++;
        }
        
        return count;
    }
    
    public void Reset(double timeSeconds = 0)
    {
        _samplePosition = (long)(timeSeconds * _sampleRate);
    }
    
    private float[] GenerateClick(double frequency, double duration, double amplitude)
    {
        int samples = (int)(_sampleRate * duration);
        var click = new float[samples];
        
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / _sampleRate;
            double envelope = Math.Exp(-t * 40); // Sharp exponential decay
            click[i] = (float)(Math.Sin(2 * Math.PI * frequency * t) * envelope * amplitude);
        }
        
        return click;
    }
}
