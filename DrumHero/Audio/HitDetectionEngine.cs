using DrumHero.Models;

namespace DrumHero.Audio;

/// <summary>
/// Compares MIDI input events against expected highway notes to determine hits and misses.
/// Uses a timing window based on difficulty setting.
/// </summary>
public class HitDetectionEngine
{
    private List<HighwayNote> _notes = new();
    private double _hitWindowSeconds;
    private int _nextExpectedIndex;

    // Tracking
    public int TotalNotes => _notes.Count;
    public int HitCount { get; private set; }
    public int MissCount { get; private set; }
    public double AccuracyPercent => TotalNotes > 0 ? (double)HitCount / TotalNotes * 100.0 : 0;

    /// <summary>
    /// Fired when a note is hit. Parameters: (HighwayNote note, double timeDifferenceMs)
    /// </summary>
    public event Action<HighwayNote, double>? NoteHit;

    /// <summary>
    /// Fired when a note is missed.
    /// </summary>
    public event Action<HighwayNote>? NoteMissed;

    /// <summary>
    /// Fired when the user hits a pad but there's no matching note (extra hit).
    /// Parameters: (DrumLane lane, double timeSeconds)
    /// </summary>
    public event Action<DrumLane, double>? ExtraHit;

    public void Initialize(List<HighwayNote> notes, DifficultyLevel difficulty)
    {
        _notes = notes.OrderBy(n => n.TimeSeconds).ToList();
        _hitWindowSeconds = DifficultySettings.GetHitWindowMs(difficulty) / 1000.0;
        _nextExpectedIndex = 0;
        HitCount = 0;
        MissCount = 0;

        foreach (var note in _notes)
        {
            note.HitState = NoteHitState.Pending;
            note.HitTimeSeconds = null;
        }
    }

    /// <summary>
    /// Process a MIDI hit event. Call this from the MIDI input callback.
    /// </summary>
    /// <param name="lane">The drum lane that was hit</param>
    /// <param name="currentTimeSeconds">Current playback time in seconds</param>
    /// <param name="velocity">MIDI velocity (0-127)</param>
    public void ProcessHit(DrumLane lane, double currentTimeSeconds, int velocity)
    {
        // Mark any notes that are now past their window as missed
        UpdateMissedNotes(currentTimeSeconds);

        // Find the nearest pending note in this lane within the hit window
        HighwayNote? bestMatch = null;
        double bestTimeDiff = double.MaxValue;

        for (int i = Math.Max(0, _nextExpectedIndex - 5); i < _notes.Count; i++)
        {
            var note = _notes[i];

            // Stop searching if we're way past the current time
            if (note.TimeSeconds > currentTimeSeconds + _hitWindowSeconds + 0.5)
                break;

            if (note.HitState != NoteHitState.Pending)
                continue;

            if (note.Lane != lane)
                continue;

            double timeDiff = Math.Abs(note.TimeSeconds - currentTimeSeconds);
            if (timeDiff <= _hitWindowSeconds && timeDiff < bestTimeDiff)
            {
                bestMatch = note;
                bestTimeDiff = timeDiff;
            }
        }

        if (bestMatch != null)
        {
            bestMatch.HitState = NoteHitState.Hit;
            bestMatch.HitTimeSeconds = currentTimeSeconds;
            HitCount++;
            NoteHit?.Invoke(bestMatch, bestTimeDiff * 1000.0); // Report in ms
        }
        else
        {
            ExtraHit?.Invoke(lane, currentTimeSeconds);
        }
    }

    /// <summary>
    /// Call this periodically to mark notes as missed once their window has passed.
    /// </summary>
    public void UpdateMissedNotes(double currentTimeSeconds)
    {
        while (_nextExpectedIndex < _notes.Count)
        {
            var note = _notes[_nextExpectedIndex];

            if (note.TimeSeconds + _hitWindowSeconds < currentTimeSeconds)
            {
                if (note.HitState == NoteHitState.Pending)
                {
                    note.HitState = NoteHitState.Missed;
                    MissCount++;
                    NoteMissed?.Invoke(note);
                }
                _nextExpectedIndex++;
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>
    /// Finalizes the session - marks all remaining pending notes as missed.
    /// </summary>
    public void FinalizeSession()
    {
        foreach (var note in _notes.Where(n => n.HitState == NoteHitState.Pending))
        {
            note.HitState = NoteHitState.Missed;
            MissCount++;
        }
    }

    public void Reset()
    {
        _nextExpectedIndex = 0;
        HitCount = 0;
        MissCount = 0;
        foreach (var note in _notes)
        {
            note.HitState = NoteHitState.Pending;
            note.HitTimeSeconds = null;
        }
    }
}
