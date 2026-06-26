using LCP.BLL.DTOs;

namespace LCP.BLL.Interfaces;

public interface IVideoService
{
    Task<List<VideoDto>> GetAllAsync(string? search = null);
    Task<PagedResult<VideoDto>> GetPagedAsync(int page, int pageSize, List<string>? tags = null, List<string>? productionInfo = null, string? search = null);
    Task<VideoDto?> GetByIdAsync(string id);
    Task<PagedResult<VideoDto>> GetByCollectionIdAsync(string collectionId, int page = 1, int pageSize = 20, string? search = null);
    Task<PagedResult<CollectionDto>> GetAllCollectionIdsAsync(int page = 1, int pageSize = 20, string? search = null);
    Task<VideoDto?> UpdateAsync(string id, UpdateVideoRequest request);
    Task<string?> ResolveFilePathAsync(string id);
    Task<VideoDto?> RegenerateSlicesAsync(string id);
    Task<PagedResult<VideoDto>> GetSimilarAsync(string id, int page = 1, int pageSize = 20);
    Task<VideoDto?> AddVideoFileAsync(string fileName, Stream content);
    Task<VideoDto?> GetRandomAsync();
}
