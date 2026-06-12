using LCP.BLL.Interfaces;
using LCP.DAL.Interfaces;

namespace LCP.BLL.Services;

public class TagService : ITagService
{
    private readonly ITagRepository _repository;
    private readonly IVideoRepository _videoRepository;

    public TagService(ITagRepository repository, IVideoRepository videoRepository)
    {
        _repository = repository;
        _videoRepository = videoRepository;
    }

    public async Task<List<string>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task AddAsync(string tag)
    {
        await _repository.AddAsync(tag);
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
            await _videoRepository.SaveAllAsync(allVideos);

        return true;
    }
}
