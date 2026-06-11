using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;

    public SettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
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
}
