using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;

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

    [AllowAnonymous]
    [HttpPost("check-password")]
    public async Task<ActionResult<bool>> CheckPassword([FromBody] PasswordRequest request)
    {
        var stored = _settings.Value.Password;
        if (string.IsNullOrEmpty(stored))
            return Unauthorized();

        if (!string.Equals(request.Password, stored, StringComparison.Ordinal))
            return Unauthorized();

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "owner")],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return Ok(true);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("session")]
    public ActionResult<bool> Session()
    {
        return User.Identity?.IsAuthenticated == true;
    }
}
