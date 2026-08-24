using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CollectionsController : ControllerBase
{
    private readonly IVideoService _videoService;
    private readonly IMediaWarmupService _warmupService;

    public CollectionsController(IVideoService videoService, IMediaWarmupService warmupService)
    {
        _videoService = videoService;
        _warmupService = warmupService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CollectionDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        return await _videoService.GetAllCollectionIdsAsync(page, pageSize, search);
    }

    [HttpGet("{collectionId}/videos")]
    public async Task<ActionResult<PagedResult<VideoDto>>> GetVideos(
        string collectionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var result = await _videoService.GetByCollectionIdAsync(collectionId, page, pageSize, search);
        _warmupService.QueueWarm(result.Items.Select(v => v.Id).ToList(), HttpContext.RequestAborted);
        return result;
    }
}
