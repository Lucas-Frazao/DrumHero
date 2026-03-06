using NAudio.Midi;

namespace DrumHero.Audio;

/// <summary>
/// Manages MIDI input from the Alesis Nitro Max (or any MIDI device).
/// Fires events when Note On messages are received.
/// </summary>
public class MidiInputEngine : IDisposable
{
    private MidiIn? _midiIn;
    private int _deviceIndex = -1;
    
    /// <summary>
    /// Fired when a MIDI Note On event is received.
    /// Parameters: (int midiNote, int velocity, double timestampMs)
    /// </summary>
    public event Action<int, int, double>? NoteOnReceived;
    
    /// <summary>
    /// Fired when any MIDI event is received (for diagnostics).
    /// </summary>
    public event Action<MidiInMessageEventArgs>? RawMidiReceived;
    
    public bool IsOpen => _midiIn != null;
    
    /// <summary>
    /// Opens the specified MIDI input device.
    /// </summary>
    public void Open(int deviceIndex)
    {
        Close();
        
        _deviceIndex = deviceIndex;
        _midiIn = new MidiIn(deviceIndex);
        _midiIn.MessageReceived += OnMidiMessageReceived;
        _midiIn.ErrorReceived += OnMidiError;
        _midiIn.Start();
    }
    
    /// <summary>
    /// Closes the current MIDI device.
    /// </summary>
    public void Close()
    {
        if (_midiIn != null)
        {
            _midiIn.Stop();
            _midiIn.MessageReceived -= OnMidiMessageReceived;
            _midiIn.ErrorReceived -= OnMidiError;
            _midiIn.Dispose();
            _midiIn = null;
        }
    }
    
    private void OnMidiMessageReceived(object? sender, MidiInMessageEventArgs e)
    {
        RawMidiReceived?.Invoke(e);
        
        if (e.MidiEvent is NoteOnEvent noteOn && noteOn.Velocity > 0)
        {
            NoteOnReceived?.Invoke(
                noteOn.NoteNumber,
                noteOn.Velocity,
                e.Timestamp); // Timestamp in ms from when device was opened
        }
    }
    
    private void OnMidiError(object? sender, MidiInMessageEventArgs e)
    {
        // Log errors but don't crash
        System.Diagnostics.Debug.WriteLine($"MIDI Error: {e.MidiEvent}");
    }
    
    /// <summary>
    /// Lists available MIDI input devices.
    /// </summary>
    public static List<(int Index, string Name)> GetMidiDevices()
    {
        var devices = new List<(int, string)>();
        for (int i = 0; i < MidiIn.NumberOfDevices; i++)
        {
            devices.Add((i, MidiIn.DeviceInfo(i).ProductName));
        }
        return devices;
    }
    
    public void Dispose()
    {
        Close();
    }
}
