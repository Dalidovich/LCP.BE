using System.IO.Compression;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LCP.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly IVideoService _videoService;
    private readonly string _libraryRootPath;

    public SystemController(
        IHostApplicationLifetime lifetime,
        IVideoService videoService,
        IOptions<LibrarySettings> settings)
    {
        _lifetime = lifetime;
        _videoService = videoService;
        _libraryRootPath = settings.Value.LibraryRootPath;
    }

    [HttpPost("shutdown")]
    public IActionResult Shutdown()
    {
        _lifetime.StopApplication();
        return Ok("Shutting down...");
    }

    [HttpGet("export/info")]
    public async Task<IActionResult> ExportInfo()
    {
        var videos = await _videoService.GetAllAsync();
        long videoBytes = 0;
        var videoCount = 0;
        foreach (var v in videos)
        {
            var path = Path.Combine(_libraryRootPath, v.RelativePath);
            var fi = new FileInfo(path);
            if (!fi.Exists) continue;
            videoBytes += fi.Length;
            videoCount++;
        }

        long systemBytes = 0;
        var systemFiles = new[] { LibrarySettings.JsonFileName, LibrarySettings.TagsFileName, LibrarySettings.ProductionInfoFileName, LibrarySettings.SettingsFileName };
        foreach (var name in systemFiles)
        {
            var path = Path.Combine(_libraryRootPath, "SYSTEMFILES", name);
            var fi = new FileInfo(path);
            if (!fi.Exists) continue;
            systemBytes += fi.Length;
        }

        return Ok(new
        {
            totalBytes = videoBytes + systemBytes,
            videoCount,
            videoBytes,
            systemBytes
        });
    }

    [HttpGet("export")]
    public async Task Export(CancellationToken ct)
    {
        var response = HttpContext.Response;
        response.ContentType = "application/zip";
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        response.Headers["Content-Disposition"] = $"attachment; filename=\"lcp-backup-{date}.zip\"";

        var syncIo = HttpContext.Features.Get<IHttpBodyControlFeature>();
        if (syncIo is not null)
            syncIo.AllowSynchronousIO = true;

        var videos = await _videoService.GetAllAsync();

        using var archive = new ZipArchive(response.Body, ZipArchiveMode.Create, leaveOpen: true);

        var systemFiles = new[]
        {
            LibrarySettings.JsonFileName,
            LibrarySettings.TagsFileName,
            LibrarySettings.ProductionInfoFileName,
            LibrarySettings.SettingsFileName,
        };

        foreach (var sysFile in systemFiles)
        {
            ct.ThrowIfCancellationRequested();
            var filePath = Path.Combine(_libraryRootPath, "SYSTEMFILES", sysFile);
            if (!System.IO.File.Exists(filePath)) continue;

            var entry = archive.CreateEntry($"SYSTEMFILES/{sysFile}", CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var fileStream = System.IO.File.OpenRead(filePath);
            await fileStream.CopyToAsync(entryStream, ct);
        }

        foreach (var video in videos)
        {
            ct.ThrowIfCancellationRequested();
            var videoPath = Path.Combine(_libraryRootPath, video.RelativePath);
            if (!System.IO.File.Exists(videoPath)) continue;

            var entry = archive.CreateEntry(video.RelativePath, CompressionLevel.NoCompression);
            using var entryStream = entry.Open();
            using var fileStream = System.IO.File.OpenRead(videoPath);
            await fileStream.CopyToAsync(entryStream, ct);
        }
    }
}
