using LCP.BLL.DTOs;
using LCP.BLL.Helpers;
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
        if (string.IsNullOrEmpty(request.Password))
            return Unauthorized();

        if (!PasswordHasher.Verify(request.Password, _settings.Value.PasswordHash, _settings.Value.PasswordSalt))
            return Unauthorized();

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "owner")],
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return Ok(true);
    }

    [HttpPost("hash-password")]
    public ActionResult<HashedPasswordDto> HashPassword(
        [FromBody] PasswordRequest request,
        [FromServices] IWebHostEnvironment environment)
    {
        if (!environment.IsDevelopment())
            return NotFound();

        if (string.IsNullOrEmpty(request.Password))
            return BadRequest();

        var hash = PasswordHasher.Hash(request.Password, out var salt);
        return new HashedPasswordDto(hash, salt);
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
