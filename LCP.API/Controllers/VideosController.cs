using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VideosController : ControllerBase
{
    private readonly IVideoService _videoService;

    public VideosController(IVideoService videoService)
    {
        _videoService = videoService;
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

    [HttpGet("{id}/stream")]
    public async Task<IActionResult> Stream(string id)
    {
        var filePath = await _videoService.ResolveFilePathAsync(id);
        if (filePath is null) return NotFound();

        var fileInfo = new FileInfo(filePath);
        var contentType = GetContentType(fileInfo.Extension);

        return PhysicalFile(filePath, contentType, enableRangeProcessing: true);
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
