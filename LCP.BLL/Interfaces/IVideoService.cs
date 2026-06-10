using LCP.BLL.DTOs;

namespace LCP.BLL.Interfaces;

public interface IVideoService
{
    Task<List<VideoDto>> GetAllAsync();
    Task<PagedResult<VideoDto>> GetPagedAsync(int page, int pageSize);
    Task<VideoDto?> GetByIdAsync(string id);
    Task<VideoDto?> UpdateAsync(string id, UpdateVideoRequest request);
    Task<bool> SoftDeleteAsync(string id);
    Task<string?> ResolveFilePathAsync(string id);
    Task<VideoDto?> RegenerateSlicesAsync(string id);
}
