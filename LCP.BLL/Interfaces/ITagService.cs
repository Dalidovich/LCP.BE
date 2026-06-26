using LCP.BLL.DTOs;
using LCP.Domain.Entities;

namespace LCP.BLL.Interfaces;

public interface ITagService
{
    Task<List<string>> GetAllAsync(List<VideoType>? videoTypeFilter = null);
    Task<List<TagInfo>> GetInfoAsync(List<VideoType>? videoTypeFilter = null);
    void InvalidateInfoCache();
    Task AddAsync(string tag);
    Task<bool> RemoveAsync(string tag);
    Task<bool> ExistsAllAsync(List<string> tags);
}
