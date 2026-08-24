using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;

namespace LCP.BLL.Services;

public class ProductionInfoService : IProductionInfoService
{
    private readonly IProductionInfoRepository _repository;
    private readonly IVideoRepository _videoRepository;
    private readonly IInfoCache<ProductionInfoDto> _infoCache;

    public ProductionInfoService(
        IProductionInfoRepository repository,
        IVideoRepository videoRepository,
        IInfoCache<ProductionInfoDto> infoCache)
    {
        _repository = repository;
        _videoRepository = videoRepository;
        _infoCache = infoCache;
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
        if (videoTypeFilter is { Count: > 0 })
            return Copy(await ComputeInfoAsync(videoTypeFilter));

        return Copy(await _infoCache.GetOrCreateAsync(() => ComputeInfoAsync(null)));
    }

    private async Task<IReadOnlyList<ProductionInfoDto>> ComputeInfoAsync(List<VideoType>? videoTypeFilter)
    {
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

        return counts
            .Select(kvp => new ProductionInfoDto { Name = kvp.Key, UsageCount = kvp.Value })
            .OrderBy(t => t.Name)
            .ToList();
    }

    private static List<ProductionInfoDto> Copy(IReadOnlyList<ProductionInfoDto> source) =>
        [.. source.Select(p => new ProductionInfoDto { Name = p.Name, UsageCount = p.UsageCount })];

    public void InvalidateInfoCache()
    {
        _infoCache.Invalidate();
    }

    public async Task AddAsync(string studio)
    {
        await _repository.AddAsync(studio);
        _infoCache.Invalidate();
    }

    public async Task<bool> ExistsAllAsync(List<string> studios)
    {
        var unknown = await GetUnknownAsync(studios);
        return unknown.Count == 0;
    }

    public async Task<List<string>> GetUnknownAsync(List<string> studios)
    {
        var masterStudios = await _repository.GetAllAsync();
        var masterSet = masterStudios.Select(t => t.ToLowerInvariant()).ToHashSet();
        return studios.Where(t => !masterSet.Contains(t.ToLowerInvariant())).ToList();
    }

    public async Task<bool> RemoveAsync(string studio)
    {
        var studios = await _repository.GetAllAsync();
        if (!studios.Contains(studio, StringComparer.OrdinalIgnoreCase)) return false;

        await _repository.RemoveAsync(studio);

        var changed = await _videoRepository.MutateAsync(videos =>
        {
            var anyRemoved = false;
            foreach (var video in videos)
            {
                var removed = video.ProductionInfo.RemoveAll(t => t.Equals(studio, StringComparison.OrdinalIgnoreCase));
                if (removed > 0) anyRemoved = true;
            }
            return (anyRemoved, anyRemoved);
        });

        if (changed) _infoCache.Invalidate();

        return true;
    }
}
