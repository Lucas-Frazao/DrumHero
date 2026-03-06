using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DrumHero.Audio;

/// <summary>
/// Manages audio playback of backing track, drum stem, and metronome click.
/// Uses NAudio with WASAPI for low-latency output.
/// </summary>
public class AudioPlaybackEngine : IDisposable
{
    private IWavePlayer? _outputDevice;
    private MixingSampleProvider? _mixer;
    private AudioFileReader? _backingTrackReader;
    private AudioFileReader? _drumStemReader;
    private VolumeSampleProvider? _backingVolume;
    private VolumeSampleProvider? _drumStemVolume;
    private MetronomeProvider? _metronomeProvider;
    private VolumeSampleProvider? _metronomeVolume;
    
    private bool _isPlaying;
    private readonly object _lock = new();
    
    public bool IsPlaying => _isPlaying;
    public TimeSpan CurrentPosition => _backingTrackReader?.CurrentTime ?? TimeSpan.Zero;
    public TimeSpan TotalDuration => _backingTrackReader?.TotalTime ?? TimeSpan.Zero;
    
    /// <summary>
    /// Initializes audio output device. Pass null for default WASAPI device.
    /// </summary>
    public void Initialize(int? deviceNumber = null, int latencyMs = 50)
    {
        Dispose();
        
        var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        var device = deviceNumber.HasValue
            ? enumerator.EnumerateAudioEndPoints(
                NAudio.CoreAudioApi.DataFlow.Render,
                NAudio.CoreAudioApi.DeviceState.Active)[deviceNumber.Value]
            : enumerator.GetDefaultAudioEndpoint(
                NAudio.CoreAudioApi.DataFlow.Render,
                NAudio.CoreAudioApi.Role.Multimedia);
        
        _outputDevice = new WasapiOut(
            device,
            NAudio.CoreAudioApi.AudioClientShareMode.Shared,
            false,
            latencyMs);
        
        // Create a mixer at 44100 Hz stereo
        _mixer = new MixingSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(44100, 2))
        {
            ReadFully = true
        };
        
        _outputDevice.Init(_mixer);
    }
    
    /// <summary>
    /// Loads audio files for a practice session.
    /// </summary>
    public void LoadSession(string backingTrackPath, string drumStemPath,
        float backingVolume = 0.8f, float drumStemVolume = 0.0f)
    {
        lock (_lock)
        {
            // Clean up previous
            ClearMixerInputs();
            
            // Load backing track
            _backingTrackReader = new AudioFileReader(backingTrackPath);
            _backingVolume = new VolumeSampleProvider(EnsureStereo44100(_backingTrackReader))
            {
                Volume = backingVolume
            };
            _mixer?.AddMixerInput(_backingVolume);
            
            // Load drum stem
            _drumStemReader = new AudioFileReader(drumStemPath);
            _drumStemVolume = new VolumeSampleProvider(EnsureStereo44100(_drumStemReader))
            {
                Volume = drumStemVolume
            };
            _mixer?.AddMixerInput(_drumStemVolume);
        }
    }
    
    /// <summary>
    /// Sets up metronome click aligned to tempo.
    /// </summary>
    public void SetupMetronome(double bpm, int beatsPerBar = 4, float volume = 0.5f)
    {
        lock (_lock)
        {
            if (_metronomeProvider != null && _mixer != null)
            {
                _mixer.RemoveMixerInput(_metronomeVolume!);
            }
            
            _metronomeProvider = new MetronomeProvider(bpm, beatsPerBar, 44100);
            _metronomeVolume = new VolumeSampleProvider(_metronomeProvider)
            {
                Volume = volume
            };
            _mixer?.AddMixerInput(_metronomeVolume);
        }
    }
    
    public void SetMetronomeEnabled(bool enabled)
    {
        if (_metronomeVolume != null)
            _metronomeVolume.Volume = enabled ? 0.5f : 0f;
    }
    
    public void SetBackingVolume(float volume)
    {
        if (_backingVolume != null) _backingVolume.Volume = volume;
    }
    
    public void SetDrumStemVolume(float volume)
    {
        if (_drumStemVolume != null) _drumStemVolume.Volume = volume;
    }
    
    public void Play()
    {
        _outputDevice?.Play();
        _isPlaying = true;
    }
    
    public void Pause()
    {
        _outputDevice?.Pause();
        _isPlaying = false;
    }
    
    public void Stop()
    {
        _outputDevice?.Stop();
        _isPlaying = false;
        Seek(TimeSpan.Zero);
    }
    
    public void Seek(TimeSpan position)
    {
        lock (_lock)
        {
            if (_backingTrackReader != null)
                _backingTrackReader.CurrentTime = position;
            if (_drumStemReader != null)
                _drumStemReader.CurrentTime = position;
            _metronomeProvider?.Reset(position.TotalSeconds);
        }
    }
    
    private void ClearMixerInputs()
    {
        _mixer?.RemoveAllMixerInputs();
        _backingTrackReader?.Dispose();
        _drumStemReader?.Dispose();
        _backingTrackReader = null;
        _drumStemReader = null;
        _backingVolume = null;
        _drumStemVolume = null;
    }
    
    private static ISampleProvider EnsureStereo44100(ISampleProvider source)
    {
        var result = source;
        
        // Resample if needed
        if (source.WaveFormat.SampleRate != 44100)
        {
            result = new WdlResamplingSampleProvider(result, 44100);
        }
        
        // Convert to stereo if mono
        if (result.WaveFormat.Channels == 1)
        {
            result = new MonoToStereoSampleProvider(result);
        }
        
        return result;
    }
    
    public void Dispose()
    {
        _isPlaying = false;
        ClearMixerInputs();
        _outputDevice?.Dispose();
        _outputDevice = null;
        _mixer = null;
    }
    
    /// <summary>
    /// Gets available audio output devices.
    /// </summary>
    public static List<(int Index, string Name)> GetOutputDevices()
    {
        var devices = new List<(int, string)>();
        var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        var endpoints = enumerator.EnumerateAudioEndPoints(
            NAudio.CoreAudioApi.DataFlow.Render,
            NAudio.CoreAudioApi.DeviceState.Active);
        
        for (int i = 0; i < endpoints.Count; i++)
        {
            devices.Add((i, endpoints[i].FriendlyName));
        }
        return devices;
    }
}
