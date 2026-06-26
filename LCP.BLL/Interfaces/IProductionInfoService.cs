using LCP.BLL.DTOs;
using LCP.Domain.Entities;

namespace LCP.BLL.Interfaces;

public interface IProductionInfoService
{
    Task<List<string>> GetAllAsync(List<VideoType>? videoTypeFilter = null);
    Task<List<ProductionInfoDto>> GetInfoAsync(List<VideoType>? videoTypeFilter = null);
    void InvalidateInfoCache();
    Task AddAsync(string studio);
    Task<bool> RemoveAsync(string studio);
    Task<bool> ExistsAllAsync(List<string> studios);
}
