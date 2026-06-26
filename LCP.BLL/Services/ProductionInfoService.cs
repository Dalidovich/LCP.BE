using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;

namespace LCP.BLL.Services;

public class ProductionInfoService : IProductionInfoService
{
    private readonly IProductionInfoRepository _repository;
    private readonly IVideoRepository _videoRepository;
    private static List<ProductionInfoDto>? _cachedInfo;

    public ProductionInfoService(IProductionInfoRepository repository, IVideoRepository videoRepository)
    {
        _repository = repository;
        _videoRepository = videoRepository;
    }

    public async Task<List<string>> GetAllAsync(List<VideoType>? videoTypeFilter = null)
    {
        if (videoTypeFilter is not { Count: > 0 })
            return await _repository.GetAllAsync();

        var filterSet = videoTypeFilter.ToHashSet();
        var allVideos = await _videoRepository.GetAllRawAsync();
        var matchingStudios = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var video in allVideos)
        {
            if (filterSet.Contains(video.Type))
            {
                foreach (var studio in video.ProductionInfo)
                    matchingStudios.Add(studio);
            }
        }
        return [.. matchingStudios.OrderBy(s => s)];
    }

    public async Task<List<ProductionInfoDto>> GetInfoAsync(List<VideoType>? videoTypeFilter = null)
    {
        if (videoTypeFilter is not { Count: > 0 })
        {
            if (_cachedInfo is not null)
                return _cachedInfo;
        }

        var allVideos = await _videoRepository.GetAllRawAsync();
        var filtered = videoTypeFilter is { Count: > 0 }
            ? allVideos.Where(v => videoTypeFilter.Contains(v.Type)).ToList()
            : allVideos;

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var video in filtered)
        {
            foreach (var studio in video.ProductionInfo)
            {
                counts.TryGetValue(studio, out var c);
                counts[studio] = c + 1;
            }
        }
        var result = counts
            .Select(kvp => new ProductionInfoDto { Name = kvp.Key, UsageCount = kvp.Value })
            .OrderBy(t => t.Name)
            .ToList();

        if (videoTypeFilter is not { Count: > 0 })
            _cachedInfo = result;

        return result;
    }

    public void InvalidateInfoCache()
    {
        _cachedInfo = null;
    }

    public async Task AddAsync(string studio)
    {
        await _repository.AddAsync(studio);
        _cachedInfo = null;
    }

    public async Task<bool> ExistsAllAsync(List<string> studios)
    {
        var masterStudios = await _repository.GetAllAsync();
        var masterSet = masterStudios.Select(t => t.ToLowerInvariant()).ToHashSet();
        return studios.All(t => masterSet.Contains(t.ToLowerInvariant()));
    }

    public async Task<bool> RemoveAsync(string studio)
    {
        var studios = await _repository.GetAllAsync();
        if (!studios.Contains(studio, StringComparer.OrdinalIgnoreCase)) return false;

        await _repository.RemoveAsync(studio);

        var allVideos = await _videoRepository.GetAllRawAsync();
        var changed = false;
        foreach (var video in allVideos)
        {
            var removed = video.ProductionInfo.RemoveAll(t => t.Equals(studio, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) changed = true;
        }
        if (changed)
        {
            await _videoRepository.SaveAllAsync(allVideos);
            _cachedInfo = null;
        }

        return true;
    }
}
