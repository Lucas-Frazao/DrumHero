using DrumHero.Models;
using Microsoft.Data.Sqlite;

namespace DrumHero.Persistence;

public class PracticeRunRepository
{
    private readonly DatabaseManager _db;
    
    public PracticeRunRepository(DatabaseManager db) => _db = db;
    
    public async Task<long> AddRunAsync(PracticeRun run)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO PracticeRuns (SongId, Timestamp, AccuracyPercent, TotalNotes,
                HitNotes, MissedNotes, Difficulty, TempoPercent, BPM, Completed)
            VALUES (@songId, @ts, @acc, @total, @hits, @misses, @diff, @tempo, @bpm, @completed);
            SELECT last_insert_rowid();";
        
        cmd.Parameters.AddWithValue("@songId", run.SongId);
        cmd.Parameters.AddWithValue("@ts", run.Timestamp.ToString("o"));
        cmd.Parameters.AddWithValue("@acc", run.AccuracyPercent);
        cmd.Parameters.AddWithValue("@total", run.TotalNotes);
        cmd.Parameters.AddWithValue("@hits", run.HitNotes);
        cmd.Parameters.AddWithValue("@misses", run.MissedNotes);
        cmd.Parameters.AddWithValue("@diff", run.Difficulty);
        cmd.Parameters.AddWithValue("@tempo", run.TempoPercent);
        cmd.Parameters.AddWithValue("@bpm", run.BPM);
        cmd.Parameters.AddWithValue("@completed", run.Completed ? 1 : 0);
        
        var result = await cmd.ExecuteScalarAsync();
        run.Id = Convert.ToInt64(result);
        return run.Id;
    }
    
    public async Task<List<PracticeRun>> GetRunsForSongAsync(long songId)
    {
        var runs = new List<PracticeRun>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM PracticeRuns WHERE SongId=@songId ORDER BY Timestamp DESC";
        cmd.Parameters.AddWithValue("@songId", songId);
        
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            runs.Add(new PracticeRun
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                SongId = reader.GetInt64(reader.GetOrdinal("SongId")),
                Timestamp = DateTime.Parse(reader.GetString(reader.GetOrdinal("Timestamp"))),
                AccuracyPercent = reader.GetDouble(reader.GetOrdinal("AccuracyPercent")),
                TotalNotes = reader.GetInt32(reader.GetOrdinal("TotalNotes")),
                HitNotes = reader.GetInt32(reader.GetOrdinal("HitNotes")),
                MissedNotes = reader.GetInt32(reader.GetOrdinal("MissedNotes")),
                Difficulty = reader.GetString(reader.GetOrdinal("Difficulty")),
                TempoPercent = reader.GetDouble(reader.GetOrdinal("TempoPercent")),
                BPM = reader.GetDouble(reader.GetOrdinal("BPM")),
                Completed = reader.GetInt32(reader.GetOrdinal("Completed")) == 1,
            });
        }
        return runs;
    }
    
    public async Task DeleteRunsForSongAsync(long songId)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM PracticeRuns WHERE SongId=@songId";
        cmd.Parameters.AddWithValue("@songId", songId);
        await cmd.ExecuteNonQueryAsync();
    }
}
