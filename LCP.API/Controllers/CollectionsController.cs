using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CollectionsController : ControllerBase
{
    private readonly IVideoService _videoService;

    public CollectionsController(IVideoService videoService)
    {
        _videoService = videoService;
    }

    [HttpGet]
    public async Task<ActionResult<List<CollectionDto>>> GetAll()
    {
        return await _videoService.GetAllCollectionIdsAsync();
    }

    [HttpGet("{collectionId}/videos")]
    public async Task<ActionResult<List<VideoDto>>> GetVideos(string collectionId)
    {
        return await _videoService.GetByCollectionIdAsync(collectionId);
    }
}
