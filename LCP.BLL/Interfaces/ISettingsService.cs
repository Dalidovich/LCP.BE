using LCP.BLL.DTOs;

namespace LCP.BLL.Interfaces;

public interface ISettingsService
{
    Task<SettingsDto> GetAsync();
    Task<SettingsDto> UpdateAsync(SettingsDto settings);
}
