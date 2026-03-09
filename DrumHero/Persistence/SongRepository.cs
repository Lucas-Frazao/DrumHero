using DrumHero.Models;
using Microsoft.Data.Sqlite;

namespace DrumHero.Persistence;

public class SongRepository
{
    private readonly DatabaseManager _db;
    
    public SongRepository(DatabaseManager db) => _db = db;
    
    public async Task<long> AddSongAsync(Song song)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Songs (Title, Artist, OriginalFilePath, OriginalMidiFilePath, DurationSeconds, BPM,
                TimeSignatureNumerator, TimeSignatureDenominator, Status, DateProcessed,
                LastPracticedDate, DrumlessTrackPath, DrumStemPath, HighwayDataPath)
            VALUES (@title, @artist, @origPath, @origMidiPath, @duration, @bpm, @tsNum, @tsDen,
                @status, @dateProc, @lastPrac, @drumless, @drumStem, @highway);
            SELECT last_insert_rowid();";
        
        cmd.Parameters.AddWithValue("@title", song.Title);
        cmd.Parameters.AddWithValue("@artist", song.Artist);
        cmd.Parameters.AddWithValue("@origPath", song.OriginalFilePath);
        cmd.Parameters.AddWithValue("@origMidiPath", song.OriginalMidiFilePath);
        cmd.Parameters.AddWithValue("@duration", song.DurationSeconds);
        cmd.Parameters.AddWithValue("@bpm", song.BPM);
        cmd.Parameters.AddWithValue("@tsNum", song.TimeSignatureNumerator);
        cmd.Parameters.AddWithValue("@tsDen", song.TimeSignatureDenominator);
        cmd.Parameters.AddWithValue("@status", (int)song.Status);
        cmd.Parameters.AddWithValue("@dateProc", song.DateProcessed.ToString("o"));
        cmd.Parameters.AddWithValue("@lastPrac", song.LastPracticedDate?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@drumless", song.DrumlessTrackPath);
        cmd.Parameters.AddWithValue("@drumStem", song.DrumStemPath);
        cmd.Parameters.AddWithValue("@highway", song.HighwayDataPath);
        
        var result = await cmd.ExecuteScalarAsync();
        song.Id = Convert.ToInt64(result);
        return song.Id;
    }
    
    public async Task UpdateSongAsync(Song song)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE Songs SET Title=@title, Artist=@artist, OriginalFilePath=@origPath,
                OriginalMidiFilePath=@origMidiPath, DurationSeconds=@duration, BPM=@bpm,
                TimeSignatureNumerator=@tsNum, TimeSignatureDenominator=@tsDen,
                Status=@status, DateProcessed=@dateProc, LastPracticedDate=@lastPrac,
                DrumlessTrackPath=@drumless, DrumStemPath=@drumStem, HighwayDataPath=@highway
            WHERE Id=@id";
        
        cmd.Parameters.AddWithValue("@id", song.Id);
        cmd.Parameters.AddWithValue("@title", song.Title);
        cmd.Parameters.AddWithValue("@artist", song.Artist);
        cmd.Parameters.AddWithValue("@origPath", song.OriginalFilePath);
        cmd.Parameters.AddWithValue("@origMidiPath", song.OriginalMidiFilePath);
        cmd.Parameters.AddWithValue("@duration", song.DurationSeconds);
        cmd.Parameters.AddWithValue("@bpm", song.BPM);
        cmd.Parameters.AddWithValue("@tsNum", song.TimeSignatureNumerator);
        cmd.Parameters.AddWithValue("@tsDen", song.TimeSignatureDenominator);
        cmd.Parameters.AddWithValue("@status", (int)song.Status);
        cmd.Parameters.AddWithValue("@dateProc", song.DateProcessed.ToString("o"));
        cmd.Parameters.AddWithValue("@lastPrac", song.LastPracticedDate?.ToString("o") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@drumless", song.DrumlessTrackPath);
        cmd.Parameters.AddWithValue("@drumStem", song.DrumStemPath);
        cmd.Parameters.AddWithValue("@highway", song.HighwayDataPath);
        
        await cmd.ExecuteNonQueryAsync();
    }
    
    public async Task<List<Song>> GetAllSongsAsync()
    {
        var songs = new List<Song>();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Songs ORDER BY DateProcessed DESC";
        
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            songs.Add(MapSong(reader));
        }
        return songs;
    }
    
    public async Task<Song?> GetSongByIdAsync(long id)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM Songs WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        
        using var reader = await cmd.ExecuteReaderAsync();
        return await reader.ReadAsync() ? MapSong(reader) : null;
    }
    
    public async Task DeleteSongAsync(long id)
    {
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Songs WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
    
    private static Song MapSong(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(reader.GetOrdinal("Id")),
        Title = reader.GetString(reader.GetOrdinal("Title")),
        Artist = reader.GetString(reader.GetOrdinal("Artist")),
        OriginalFilePath = reader.GetString(reader.GetOrdinal("OriginalFilePath")),
        OriginalMidiFilePath = reader.IsDBNull(reader.GetOrdinal("OriginalMidiFilePath"))
            ? string.Empty : reader.GetString(reader.GetOrdinal("OriginalMidiFilePath")),
        DurationSeconds = reader.GetDouble(reader.GetOrdinal("DurationSeconds")),
        BPM = reader.GetDouble(reader.GetOrdinal("BPM")),
        TimeSignatureNumerator = reader.GetInt32(reader.GetOrdinal("TimeSignatureNumerator")),
        TimeSignatureDenominator = reader.GetInt32(reader.GetOrdinal("TimeSignatureDenominator")),
        Status = (SongStatus)reader.GetInt32(reader.GetOrdinal("Status")),
        DateProcessed = DateTime.Parse(reader.GetString(reader.GetOrdinal("DateProcessed"))),
        LastPracticedDate = reader.IsDBNull(reader.GetOrdinal("LastPracticedDate")) 
            ? null : DateTime.Parse(reader.GetString(reader.GetOrdinal("LastPracticedDate"))),
        DrumlessTrackPath = reader.GetString(reader.GetOrdinal("DrumlessTrackPath")),
        DrumStemPath = reader.GetString(reader.GetOrdinal("DrumStemPath")),
        HighwayDataPath = reader.GetString(reader.GetOrdinal("HighwayDataPath")),
    };
}
