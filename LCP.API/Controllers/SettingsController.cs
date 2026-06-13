using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;
    private readonly IOptions<LibrarySettings> _settings;

    public SettingsController(ISettingsService settingsService, IOptions<LibrarySettings> settings)
    {
        _settingsService = settingsService;
        _settings = settings;
    }

    [HttpGet]
    public async Task<ActionResult<SettingsDto>> Get()
    {
        return await _settingsService.GetAsync();
    }

    [HttpPut]
    public async Task<ActionResult<SettingsDto>> Update(SettingsDto settings)
    {
        var result = await _settingsService.UpdateAsync(settings);
        return result;
    }

    [HttpPost("check-password")]
    public ActionResult<bool> CheckPassword([FromBody] PasswordRequest request)
    {
        var stored = _settings.Value.Password;
        if (string.IsNullOrEmpty(stored))
            return false;

        return string.Equals(request.Password, stored, StringComparison.Ordinal);
    }
}
