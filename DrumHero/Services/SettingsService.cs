using DrumHero.Models;
using DrumHero.Persistence;

namespace DrumHero.Services;

public class SettingsService
{
    private readonly SettingsRepository _repository;
    private AppSettings? _cachedSettings;
    
    public SettingsService(SettingsRepository repository)
    {
        _repository = repository;
    }
    
    public async Task<AppSettings> GetSettingsAsync()
    {
        _cachedSettings ??= await _repository.LoadSettingsAsync();
        return _cachedSettings;
    }
    
    public async Task SaveSettingsAsync(AppSettings settings)
    {
        _cachedSettings = settings;
        await _repository.SaveSettingsAsync(settings);
    }
}
