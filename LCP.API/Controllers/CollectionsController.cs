using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CollectionsController : ControllerBase
{
    private readonly IVideoService _videoService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IPreviewService _previewService;
    private readonly ISettingsRepository _settingsRepository;

    public CollectionsController(
        IVideoService videoService,
        IThumbnailService thumbnailService,
        IPreviewService previewService,
        ISettingsRepository settingsRepository)
    {
        _videoService = videoService;
        _thumbnailService = thumbnailService;
        _previewService = previewService;
        _settingsRepository = settingsRepository;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CollectionDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        return await _videoService.GetAllCollectionIdsAsync(page, pageSize);
    }

    [HttpGet("{collectionId}/videos")]
    public async Task<ActionResult<PagedResult<VideoDto>>> GetVideos(
        string collectionId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        var result = await _videoService.GetByCollectionIdAsync(collectionId, page, pageSize);
        _ = WarmCacheAsync(result.Items);
        return result;
    }

    private async Task WarmCacheAsync(List<VideoDto> videos)
    {
        var settings = await _settingsRepository.GetAsync();
        if (settings is null || !settings.WarmCache || videos.Count == 0)
            return;

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 2 };
        await Parallel.ForEachAsync(videos, parallelOptions, async (video, ct) =>
        {
            await _thumbnailService.GetThumbnailAsync(video.Id);
            await _previewService.GetPreviewAsync(video.Id, PreviewResolution.Preview144);
        });
    }
}
