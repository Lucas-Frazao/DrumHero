using NAudio.Wave;
using SoundTouch;

namespace DrumHero.Audio;

/// <summary>
/// Wraps SoundTouch.Net for real-time time-stretching of audio without pitch change.
/// </summary>
public class TimeStretchProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly SoundTouchProcessor _soundTouch;
    private readonly float[] _sourceBuffer;
    private readonly float[] _outputBuffer;
    private int _outputBufferCount;
    private int _outputBufferOffset;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public TimeStretchProvider(ISampleProvider source, double tempoChangePercent)
    {
        _source = source;
        _soundTouch = new SoundTouchProcessor();
        _soundTouch.SampleRate = source.WaveFormat.SampleRate;
        _soundTouch.Channels = source.WaveFormat.Channels;
        _soundTouch.TempoChange = tempoChangePercent; // e.g., -50 for half speed, +100 for double
        _soundTouch.SetSetting(SettingId.UseQuickSeek, 0);
        _soundTouch.SetSetting(SettingId.UseAntiAliasFilter, 1);

        _sourceBuffer = new float[4096];
        _outputBuffer = new float[8192];
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int totalRead = 0;

        while (totalRead < count)
        {
            // First, drain any buffered output
            if (_outputBufferOffset < _outputBufferCount)
            {
                int available = _outputBufferCount - _outputBufferOffset;
                int toCopy = Math.Min(available, count - totalRead);
                Array.Copy(_outputBuffer, _outputBufferOffset, buffer, offset + totalRead, toCopy);
                _outputBufferOffset += toCopy;
                totalRead += toCopy;
                continue;
            }

            // Try to get processed samples from SoundTouch
            int received = (int)_soundTouch.ReceiveSamples(_outputBuffer, (int)(_outputBuffer.Length / WaveFormat.Channels));
            if (received > 0)
            {
                _outputBufferCount = received * WaveFormat.Channels;
                _outputBufferOffset = 0;
                continue;
            }

            // Feed more source samples to SoundTouch
            int sourceRead = _source.Read(_sourceBuffer, 0, _sourceBuffer.Length);
            if (sourceRead == 0)
            {
                _soundTouch.Flush();
                received = (int)_soundTouch.ReceiveSamples(_outputBuffer, (int)(_outputBuffer.Length / WaveFormat.Channels));
                if (received > 0)
                {
                    _outputBufferCount = received * WaveFormat.Channels;
                    _outputBufferOffset = 0;
                    continue;
                }
                break; // No more data
            }

            _soundTouch.PutSamples(_sourceBuffer, (int)(sourceRead / WaveFormat.Channels));
        }

        return totalRead;
    }

    /// <summary>
    /// Changes the tempo percentage at runtime.
    /// </summary>
    public void SetTempoChange(double tempoChangePercent)
    {
        _soundTouch.TempoChange = tempoChangePercent;
    }
}

/// <summary>
/// Static helper for tempo calculations.
/// </summary>
public static class TimeStretchEngine
{
    /// <summary>
    /// Converts a speed percentage (25%, 50%, etc.) to SoundTouch tempo change value.
    /// Speed 100% = 0 change, 50% = -50, 200% = +100
    /// </summary>
    public static double SpeedPercentToTempoChange(double speedPercent)
    {
        return speedPercent - 100.0;
    }

    /// <summary>
    /// Converts a target BPM to the required speed percentage given the original BPM.
    /// </summary>
    public static double BpmToSpeedPercent(double originalBpm, double targetBpm)
    {
        return (targetBpm / originalBpm) * 100.0;
    }

    /// <summary>
    /// Available speed presets as per spec.
    /// </summary>
    public static readonly double[] SpeedPresets = { 25, 50, 75, 100, 125 };
}
