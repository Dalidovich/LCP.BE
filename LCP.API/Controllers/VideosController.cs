using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VideosController : ControllerBase
{
    private readonly IVideoService _videoService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IPreviewService _previewService;

    public VideosController(IVideoService videoService, IThumbnailService thumbnailService, IPreviewService previewService)
    {
        _videoService = videoService;
        _thumbnailService = thumbnailService;
        _previewService = previewService;
    }

    [HttpGet]
    public async Task<ActionResult<List<VideoDto>>> GetAll()
    {
        return await _videoService.GetAllAsync();
    }

    [HttpGet("paged")]
    public async Task<ActionResult<PagedResult<VideoDto>>> GetPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;

        return await _videoService.GetPagedAsync(page, pageSize);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<VideoDto>> GetById(string id)
    {
        var video = await _videoService.GetByIdAsync(id);
        if (video is null) return NotFound();
        return video;
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<VideoDto>> Update(string id, UpdateVideoRequest request)
    {
        var video = await _videoService.UpdateAsync(id, request);
        if (video is null) return NotFound();
        return video;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> SoftDelete(string id)
    {
        var deleted = await _videoService.SoftDeleteAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }

    [HttpPost("{id}/regenerate-slices")]
    public async Task<ActionResult> RegenerateSlices(string id)
    {
        var video = await _videoService.RegenerateSlicesAsync(id);
        if (video is null) return NotFound();
        return NoContent();
    }

    [HttpGet("{id}/stream")]
    public async Task<IActionResult> Stream(string id)
    {
        var filePath = await _videoService.ResolveFilePathAsync(id);
        if (filePath is null) return NotFound();

        var fileInfo = new FileInfo(filePath);
        var contentType = GetContentType(fileInfo.Extension);

        return PhysicalFile(filePath, contentType, enableRangeProcessing: true);
    }

    [HttpGet("{id}/preview")]
    public async Task<IActionResult> Preview(string id, [FromQuery] PreviewResolution resolution = PreviewResolution.Preview144)
    {
        var result = await _previewService.GetPreviewAsync(id, resolution);
        if (result is null) return NotFound();

        var etag = $"\"{result.LastModified.Ticks:x}\"";

        if (Request.Headers.IfNoneMatch.ToString() == etag)
            return StatusCode(304);

        Response.Headers.ETag = etag;
        Response.Headers.LastModified = result.LastModified.ToString("R");
        Response.Headers.AcceptRanges = "bytes";

        return File(result.Data, "video/mp4", enableRangeProcessing: true);
    }

    [HttpGet("{id}/thumbnail")]
    public async Task<IActionResult> Thumbnail(string id, [FromQuery] double? t = null, [FromQuery] bool noCache = false)
    {
        ThumbnailResult? result;

        if (t.HasValue)
        {
            result = await _thumbnailService.GetThumbnailPreviewAsync(id, t.Value);
        }
        else
        {
            if (noCache)
                _thumbnailService.InvalidateCache(id);
            result = await _thumbnailService.GetThumbnailAsync(id);
        }

        if (result is null) return NotFound();

        var etag = $"\"{result.LastModified.Ticks:x}\"";

        if (Request.Headers.IfNoneMatch.ToString() == etag)
            return StatusCode(304);

        Response.Headers.ETag = etag;
        Response.Headers.LastModified = result.LastModified.ToString("R");

        return File(result.Data, "image/jpeg");
    }

    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".flv" => "video/x-flv",
            ".webm" => "video/webm",
            ".m4v" => "video/mp4",
            ".ts" => "video/mp2t",
            _ => "application/octet-stream"
        };
    }
}
