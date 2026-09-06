using LCP.DAL.Interfaces;
using LCP.Domain.Entities;

namespace LCP.Tests.Fakes;

public sealed class InMemoryVideoRepository : IVideoRepository
{
    private IReadOnlyList<VideoMetadata> _entries;

    public InMemoryVideoRepository(params VideoMetadata[] entries)
    {
        _entries = [.. entries.Select(e => e.Clone())];
    }

    public int SaveCount { get; private set; }

    public Task<IReadOnlyList<VideoMetadata>> GetSnapshotAsync() => Task.FromResult(_entries);

    public Task<VideoMetadata?> GetByIdAsync(string id) =>
        Task.FromResult(_entries.FirstOrDefault(v => v.Id == id));

    public Task<IReadOnlyList<VideoMetadata>> GetByCollectionIdAsync(string collectionId) =>
        Task.FromResult<IReadOnlyList<VideoMetadata>>([.. _entries.Where(v => v.CollectionId == collectionId)]);

    public Task<List<(string Id, int Count)>> GetAllCollectionIdsAsync() =>
        Task.FromResult(_entries
            .Where(v => v.CollectionId is not null)
            .GroupBy(v => v.CollectionId!)
            .Select(g => (Id: g.Key, Count: g.Count()))
            .ToList());

    public Task SaveAllAsync(List<VideoMetadata> videos)
    {
        _entries = [.. videos.Select(v => v.Clone())];
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<T> MutateAsync<T>(Func<List<VideoMetadata>, (bool Changed, T Result)> mutation)
    {
        var working = _entries.Select(v => v.Clone()).ToList();
        var (changed, result) = mutation(working);
        if (changed)
        {
            _entries = working;
            SaveCount++;
        }
        return Task.FromResult(result);
    }

    public Task InvalidateCacheAsync() => Task.CompletedTask;
}
