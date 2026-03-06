using System.IO;

namespace DrumHero.Persistence;

public class FileStorageService
{
    public string BaseDataPath { get; }
    public string SongsPath { get; }

    public FileStorageService()
    {
        BaseDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DrumHero");
        SongsPath = Path.Combine(BaseDataPath, "Songs");

        Directory.CreateDirectory(BaseDataPath);
        Directory.CreateDirectory(SongsPath);
    }

    public string GetDatabasePath() => Path.Combine(BaseDataPath, "drumhero.db");

    /// <summary>
    /// Creates a directory for a song's assets and returns the path.
    /// </summary>
    public string GetSongDirectory(long songId)
    {
        var dir = Path.Combine(SongsPath, songId.ToString());
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Returns the full path for a song asset file.
    /// </summary>
    public string GetSongAssetPath(long songId, string filename)
    {
        return Path.Combine(GetSongDirectory(songId), filename);
    }

    /// <summary>
    /// Resolves a relative asset path to an absolute path.
    /// </summary>
    public string ResolveAssetPath(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            return relativePath;
        return Path.Combine(BaseDataPath, relativePath);
    }

    /// <summary>
    /// Deletes all assets for a song.
    /// </summary>
    public void DeleteSongAssets(long songId)
    {
        var dir = Path.Combine(SongsPath, songId.ToString());
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Copies a file into the song's asset directory and returns the relative path.
    /// </summary>
    public async Task<string> StoreSongAssetAsync(long songId, string sourceFilePath, string targetFileName)
    {
        var targetPath = GetSongAssetPath(songId, targetFileName);

        using var source = File.OpenRead(sourceFilePath);
        using var target = File.Create(targetPath);
        await source.CopyToAsync(target);

        // Return path relative to songs directory
        return Path.Combine("Songs", songId.ToString(), targetFileName);
    }

    /// <summary>
    /// Writes bytes to a song asset file and returns the relative path.
    /// </summary>
    public async Task<string> WriteSongAssetAsync(long songId, string fileName, byte[] data)
    {
        var targetPath = GetSongAssetPath(songId, fileName);
        await File.WriteAllBytesAsync(targetPath, data);
        return Path.Combine("Songs", songId.ToString(), fileName);
    }

    /// <summary>
    /// Writes text to a song asset file and returns the relative path.
    /// </summary>
    public async Task<string> WriteSongAssetTextAsync(long songId, string fileName, string text)
    {
        var targetPath = GetSongAssetPath(songId, fileName);
        await File.WriteAllTextAsync(targetPath, text);
        return Path.Combine("Songs", songId.ToString(), fileName);
    }
}
