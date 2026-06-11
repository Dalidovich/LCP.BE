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
    public async Task<ActionResult<List<CollectionDto>>> GetAll()
    {
        return await _videoService.GetAllCollectionIdsAsync();
    }

    [HttpGet("{collectionId}/videos")]
    public async Task<ActionResult<List<VideoDto>>> GetVideos(string collectionId)
    {
        var videos = await _videoService.GetByCollectionIdAsync(collectionId);
        _ = WarmCacheAsync(videos);
        return videos;
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
