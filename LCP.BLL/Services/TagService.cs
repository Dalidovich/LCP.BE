using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Interfaces;

namespace LCP.BLL.Services;

public class TagService : ITagService
{
    private readonly ITagRepository _repository;
    private readonly IVideoRepository _videoRepository;
    private static List<TagInfo>? _cachedInfo;

    public TagService(ITagRepository repository, IVideoRepository videoRepository)
    {
        _repository = repository;
        _videoRepository = videoRepository;
    }

    public async Task<List<string>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task<List<TagInfo>> GetInfoAsync()
    {
        if (_cachedInfo is not null)
            return _cachedInfo;

        var allVideos = await _videoRepository.GetAllRawAsync();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var video in allVideos)
        {
            foreach (var tag in video.Tags)
            {
                counts.TryGetValue(tag, out var c);
                counts[tag] = c + 1;
            }
        }
        _cachedInfo = counts
            .Select(kvp => new TagInfo { Tag = kvp.Key, UsageCount = kvp.Value })
            .OrderBy(t => t.Tag)
            .ToList();
        return _cachedInfo;
    }

    public void InvalidateInfoCache()
    {
        _cachedInfo = null;
    }

    public async Task AddAsync(string tag)
    {
        await _repository.AddAsync(tag);
        _cachedInfo = null;
    }

    public async Task<bool> ExistsAllAsync(List<string> tags)
    {
        var masterTags = await _repository.GetAllAsync();
        var masterSet = masterTags.Select(t => t.ToLowerInvariant()).ToHashSet();
        return tags.All(t => masterSet.Contains(t.ToLowerInvariant()));
    }

    public async Task<bool> RemoveAsync(string tag)
    {
        var tags = await _repository.GetAllAsync();
        if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return false;

        await _repository.RemoveAsync(tag);

        var allVideos = await _videoRepository.GetAllRawAsync();
        var changed = false;
        foreach (var video in allVideos)
        {
            var removed = video.Tags.RemoveAll(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
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
