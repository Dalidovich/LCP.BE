using LCP.Domain.Entities;

namespace LCP.DAL.Interfaces;

public interface ISettingsRepository
{
    Task<SiteSettings?> GetAsync();
    Task UpdateAsync(SiteSettings settings);
}
