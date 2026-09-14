using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/most-watched")]
public class MostWatchedController : ControllerBase
{
    private readonly IWatchRecordService _watchRecordService;
    private readonly IVideoService _videoService;
    private readonly ISettingsService _settingsService;

    public MostWatchedController(
        IWatchRecordService watchRecordService,
        IVideoService videoService,
        ISettingsService settingsService)
    {
        _watchRecordService = watchRecordService;
        _videoService = videoService;
        _settingsService = settingsService;
    }

    [HttpPost]
    public async Task<IActionResult> Record(WatchRecordRequest request)
    {
        var settings = await _settingsService.GetAsync();
        if (!settings.MostWatched)
            return Conflict(new { error = "Most watched collection is disabled" });

        if (request.Segments.Any(s => s.Start < 0 || s.Duration < 0))
            return BadRequest(new { error = "Segment start and duration must not be negative" });

        if (await _videoService.GetByIdAsync(request.VideoId) is null)
            return NotFound();

        await _watchRecordService.RecordAsync(request.VideoId, request.Segments);
        return NoContent();
    }
}
