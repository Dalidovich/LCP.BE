using LCP.BLL.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SyncController : ControllerBase
{
    private readonly ILibrarySyncService _syncService;

    public SyncController(ILibrarySyncService syncService)
    {
        _syncService = syncService;
    }

    [HttpPost]
    public async Task<ActionResult> Sync()
    {
        await _syncService.SyncAsync();
        return Ok();
    }
}
