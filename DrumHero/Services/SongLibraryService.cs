using DrumHero.Models;
using DrumHero.Persistence;

namespace DrumHero.Services;

public class SongLibraryService
{
    private readonly SongRepository _songRepo;
    private readonly PracticeRunRepository _runRepo;
    private readonly FileStorageService _fileStorage;
    
    public SongLibraryService(SongRepository songRepo, PracticeRunRepository runRepo, FileStorageService fileStorage)
    {
        _songRepo = songRepo;
        _runRepo = runRepo;
        _fileStorage = fileStorage;
    }
    
    public Task<List<Song>> GetAllSongsAsync() => _songRepo.GetAllSongsAsync();
    
    public Task<Song?> GetSongByIdAsync(long id) => _songRepo.GetSongByIdAsync(id);
    
    public async Task DeleteSongAsync(long songId)
    {
        await _runRepo.DeleteRunsForSongAsync(songId);
        await _songRepo.DeleteSongAsync(songId);
        _fileStorage.DeleteSongAssets(songId);
    }
    
    public Task<List<PracticeRun>> GetPracticeHistoryAsync(long songId)
        => _runRepo.GetRunsForSongAsync(songId);
    
    public async Task UpdateLastPracticedAsync(long songId)
    {
        var song = await _songRepo.GetSongByIdAsync(songId);
        if (song != null)
        {
            song.LastPracticedDate = DateTime.Now;
            await _songRepo.UpdateSongAsync(song);
        }
    }
}
