using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/compilation")]
public class CompilationController : ControllerBase
{
    private readonly ICompilationService _compilationService;

    public CompilationController(ICompilationService compilationService)
    {
        _compilationService = compilationService;
    }

    [HttpPost]
    public async Task<ActionResult<CompilationDto>> Build([FromQuery] bool rebuild = false)
    {
        var compilation = await _compilationService.BuildAsync(rebuild);
        if (compilation is null)
            return NotFound(new { error = "Not enough watch data for a compilation" });

        return compilation;
    }

    [HttpGet("{id}/stream")]
    public IActionResult Stream(string id)
    {
        var data = _compilationService.GetData(id);
        if (data is null) return NotFound();

        Response.Headers.CacheControl = "no-store";
        return File(data, "video/mp4", enableRangeProcessing: true);
    }
}
