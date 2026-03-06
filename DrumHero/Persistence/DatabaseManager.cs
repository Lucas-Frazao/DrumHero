using Microsoft.Data.Sqlite;

namespace DrumHero.Persistence;

public class DatabaseManager : IDisposable
{
    private readonly string _connectionString;
    
    public DatabaseManager(string databasePath)
    {
        _connectionString = $"Data Source={databasePath}";
        InitializeDatabase();
    }
    
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        // Enable WAL mode for better concurrent read performance
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();
        return connection;
    }
    
    private void InitializeDatabase()
    {
        using var connection = CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Songs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Artist TEXT NOT NULL DEFAULT '',
                OriginalFilePath TEXT NOT NULL,
                DurationSeconds REAL NOT NULL DEFAULT 0,
                BPM REAL NOT NULL DEFAULT 120,
                TimeSignatureNumerator INTEGER NOT NULL DEFAULT 4,
                TimeSignatureDenominator INTEGER NOT NULL DEFAULT 4,
                Status INTEGER NOT NULL DEFAULT 0,
                DateProcessed TEXT NOT NULL,
                LastPracticedDate TEXT,
                DrumlessTrackPath TEXT NOT NULL DEFAULT '',
                DrumStemPath TEXT NOT NULL DEFAULT '',
                HighwayDataPath TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS PracticeRuns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SongId INTEGER NOT NULL,
                Timestamp TEXT NOT NULL,
                AccuracyPercent REAL NOT NULL DEFAULT 0,
                TotalNotes INTEGER NOT NULL DEFAULT 0,
                HitNotes INTEGER NOT NULL DEFAULT 0,
                MissedNotes INTEGER NOT NULL DEFAULT 0,
                Difficulty TEXT NOT NULL DEFAULT 'Normal',
                TempoPercent REAL NOT NULL DEFAULT 100,
                BPM REAL NOT NULL DEFAULT 120,
                Completed INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (SongId) REFERENCES Songs(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_practice_runs_song ON PracticeRuns(SongId);
            CREATE INDEX IF NOT EXISTS idx_practice_runs_timestamp ON PracticeRuns(Timestamp);
        ";
        cmd.ExecuteNonQuery();
    }
    
    public void Dispose()
    {
        // SQLite connections are lightweight; nothing special to dispose at manager level
    }
}
