using LCP.BLL.DTOs;

namespace LCP.BLL.Interfaces;

public interface ITagService
{
    Task<List<string>> GetAllAsync();
    Task<List<TagInfo>> GetInfoAsync();
    void InvalidateInfoCache();
    Task AddAsync(string tag);
    Task<bool> RemoveAsync(string tag);
    Task<bool> ExistsAllAsync(List<string> tags);
}
