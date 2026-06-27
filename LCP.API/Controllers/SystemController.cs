using Microsoft.AspNetCore.Mvc;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly IHostApplicationLifetime _lifetime;

    public SystemController(IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    [HttpPost("shutdown")]
    public IActionResult Shutdown()
    {
        _lifetime.StopApplication();
        return Ok("Shutting down...");
    }
}
