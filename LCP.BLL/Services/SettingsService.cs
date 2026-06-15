using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;

namespace LCP.BLL.Services;

public class SettingsService : ISettingsService
{
    private readonly ISettingsRepository _repository;

    public SettingsService(ISettingsRepository repository)
    {
        _repository = repository;
    }

    public async Task<SettingsDto> GetAsync()
    {
        var settings = await _repository.GetAsync();
        return MapToDto(settings ?? new SiteSettings());
    }

    public async Task<SettingsDto> UpdateAsync(SettingsDto settings)
    {
        var entity = new SiteSettings
        {
            Theme = settings.Theme,
            AnimeSpeedUp = settings.AnimeSpeedUp,
            WarmCache = settings.WarmCache,
            RandomSort = settings.RandomSort,
            Debug = settings.Debug,
            StatisticsMode = settings.StatisticsMode,
            VideoTypeFilter = settings.VideoTypeFilter
        };

        await _repository.UpdateAsync(entity);
        return settings;
    }

    private static SettingsDto MapToDto(SiteSettings s) => new()
    {
        Theme = s.Theme,
        AnimeSpeedUp = s.AnimeSpeedUp,
        WarmCache = s.WarmCache,
        RandomSort = s.RandomSort,
        Debug = s.Debug,
        StatisticsMode = s.StatisticsMode,
        VideoTypeFilter = s.VideoTypeFilter
    };
}
