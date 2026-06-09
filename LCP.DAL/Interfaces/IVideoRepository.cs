using LCP.Domain.Entities;

namespace LCP.DAL.Interfaces;

public interface IVideoRepository
{
    Task<List<VideoMetadata>> GetAllAsync();
    Task<List<VideoMetadata>> GetAllRawAsync();
    Task<VideoMetadata?> GetByIdAsync(string id);
    Task<(List<VideoMetadata> Items, int TotalCount)> GetPagedAsync(int page, int pageSize);
    Task SoftDeleteAsync(string id);
    Task SaveAllAsync(List<VideoMetadata> videos);
}
