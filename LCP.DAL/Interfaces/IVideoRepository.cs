using LCP.Domain.Entities;

namespace LCP.DAL.Interfaces;

public interface IVideoRepository
{
    Task<IReadOnlyList<VideoMetadata>> GetSnapshotAsync();
    Task<VideoMetadata?> GetByIdAsync(string id);
    Task<IReadOnlyList<VideoMetadata>> GetByCollectionIdAsync(string collectionId);
    Task<List<(string Id, int Count)>> GetAllCollectionIdsAsync();
    Task SaveAllAsync(List<VideoMetadata> videos);
    Task<T> MutateAsync<T>(Func<List<VideoMetadata>, (bool Changed, T Result)> mutation);
    Task InvalidateCacheAsync();
}
