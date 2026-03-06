using DrumHero.Models;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace DrumHero.Persistence;

public class SettingsRepository
{
    private readonly DatabaseManager _db;

    public SettingsRepository(DatabaseManager db) => _db = db;

    public async Task<AppSettings> LoadSettingsAsync()
    {
        var settings = new AppSettings();
        using var conn = _db.CreateConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key, Value FROM Settings";

        var dict = new Dictionary<string, string>();
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            dict[reader.GetString(0)] = reader.GetString(1);
        }

        if (dict.TryGetValue("AudioOutputDeviceId", out var audioOut)) settings.AudioOutputDeviceId = audioOut;
        if (dict.TryGetValue("MidiInputDeviceId", out var midiIn)) settings.MidiInputDeviceId = midiIn;
        if (dict.TryGetValue("Difficulty", out var diff) && Enum.TryParse<DifficultyLevel>(diff, out var d)) settings.Difficulty = d;
        if (dict.TryGetValue("AnalysisApiBaseUrl", out var apiUrl)) settings.AnalysisApiBaseUrl = apiUrl;
        if (dict.TryGetValue("AnalysisApiKey", out var apiKey)) settings.AnalysisApiKey = apiKey;
        if (dict.TryGetValue("AudioBufferSizeMs", out var buf) && int.TryParse(buf, out var b)) settings.AudioBufferSizeMs = b;
        if (dict.TryGetValue("MetronomeEnabled", out var met)) settings.MetronomeEnabled = met == "true";
        if (dict.TryGetValue("CountInBars", out var ci) && int.TryParse(ci, out var c)) settings.CountInBars = c;
        if (dict.TryGetValue("MasterVolume", out var mv) && double.TryParse(mv, out var mvv)) settings.MasterVolume = mvv;
        if (dict.TryGetValue("MetronomeVolume", out var metv) && double.TryParse(metv, out var metvv)) settings.MetronomeVolume = metvv;
        if (dict.TryGetValue("DrumStemVolume", out var dsv) && double.TryParse(dsv, out var dsvv)) settings.DrumStemVolume = dsvv;
        if (dict.TryGetValue("BackingTrackVolume", out var btv) && double.TryParse(btv, out var btvv)) settings.BackingTrackVolume = btvv;
        if (dict.TryGetValue("InputTimingOffsetMs", out var ito) && int.TryParse(ito, out var itov)) settings.InputTimingOffsetMs = itov;

        return settings;
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        using var conn = _db.CreateConnection();
        using var transaction = conn.BeginTransaction();

        var pairs = new Dictionary<string, string>
        {
            ["AudioOutputDeviceId"] = settings.AudioOutputDeviceId ?? "",
            ["MidiInputDeviceId"] = settings.MidiInputDeviceId ?? "",
            ["Difficulty"] = settings.Difficulty.ToString(),
            ["AnalysisApiBaseUrl"] = settings.AnalysisApiBaseUrl ?? "",
            ["AnalysisApiKey"] = settings.AnalysisApiKey ?? "",
            ["AudioBufferSizeMs"] = settings.AudioBufferSizeMs.ToString(),
            ["MetronomeEnabled"] = settings.MetronomeEnabled ? "true" : "false",
            ["CountInBars"] = settings.CountInBars.ToString(),
            ["MasterVolume"] = settings.MasterVolume.ToString("F2"),
            ["MetronomeVolume"] = settings.MetronomeVolume.ToString("F2"),
            ["DrumStemVolume"] = settings.DrumStemVolume.ToString("F2"),
            ["BackingTrackVolume"] = settings.BackingTrackVolume.ToString("F2"),
            ["InputTimingOffsetMs"] = settings.InputTimingOffsetMs.ToString(),
        };

        foreach (var (key, value) in pairs)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = transaction;
            cmd.CommandText = @"
                INSERT INTO Settings (Key, Value) VALUES (@key, @value)
                ON CONFLICT(Key) DO UPDATE SET Value=excluded.Value";
            cmd.Parameters.AddWithValue("@key", key);
            cmd.Parameters.AddWithValue("@value", value);
            await cmd.ExecuteNonQueryAsync();
        }

        transaction.Commit();
    }
}
