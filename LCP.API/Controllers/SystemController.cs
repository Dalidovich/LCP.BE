using System.IO.Compression;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
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
    private readonly IVideoRepository _videoRepository;
    private readonly ITagRepository _tagRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IProductionInfoRepository _productionInfoRepository;
    private readonly IThumbnailService _thumbnailService;
    private readonly IPreviewService _previewService;
    private readonly string _libraryRootPath;

    public SystemController(
        IHostApplicationLifetime lifetime,
        IVideoService videoService,
        IVideoRepository videoRepository,
        ITagRepository tagRepository,
        ISettingsRepository settingsRepository,
        IProductionInfoRepository productionInfoRepository,
        IThumbnailService thumbnailService,
        IPreviewService previewService,
        IOptions<LibrarySettings> settings)
    {
        _lifetime = lifetime;
        _videoService = videoService;
        _videoRepository = videoRepository;
        _tagRepository = tagRepository;
        _settingsRepository = settingsRepository;
        _productionInfoRepository = productionInfoRepository;
        _thumbnailService = thumbnailService;
        _previewService = previewService;
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

    [HttpPost("import")]
    [RequestSizeLimit(200L * 1024 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 200L * 1024 * 1024 * 1024)]
    public async Task<IActionResult> Import(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "No file uploaded" });

        // 1. Clear LibraryRootPath
        var rootDir = new DirectoryInfo(_libraryRootPath);
        if (rootDir.Exists)
        {
            foreach (var f in rootDir.GetFiles())
            {
                ct.ThrowIfCancellationRequested();
                try { f.Delete(); } catch { /* skip locked files */ }
            }
            foreach (var sub in rootDir.GetDirectories())
            {
                ct.ThrowIfCancellationRequested();
                try { sub.Delete(true); } catch { /* skip locked dirs */ }
            }
        }

        // 2. Extract ZIP contents
        using var archive = new ZipArchive(file.OpenReadStream(), ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var entryPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(_libraryRootPath, entryPath);
            var entryDir = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
                Directory.CreateDirectory(entryDir);

            entry.ExtractToFile(fullPath, overwrite: true);
        }

        // 3. Ensure system files exist (create empty defaults if missing)
        var systemFiles = new[]
        {
            LibrarySettings.JsonFileName,
            LibrarySettings.TagsFileName,
            LibrarySettings.SettingsFileName,
            LibrarySettings.ProductionInfoFileName,
        };

        foreach (var sysFile in systemFiles)
        {
            var filePath = Path.Combine(_libraryRootPath, "SYSTEMFILES", sysFile);
            if (!System.IO.File.Exists(filePath))
            {
                var sysDir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(sysDir) && !Directory.Exists(sysDir))
                    Directory.CreateDirectory(sysDir);

                var defaultContent = sysFile == LibrarySettings.SettingsFileName
                    ? "{}"
                    : "[]";
                await System.IO.File.WriteAllTextAsync(filePath, defaultContent, ct);
            }
        }

        // 4. Invalidate all caches
        await _videoRepository.InvalidateCacheAsync();
        await _tagRepository.InvalidateCacheAsync();
        await _settingsRepository.InvalidateCacheAsync();
        await _productionInfoRepository.InvalidateCacheAsync();
        _thumbnailService.ClearAllCache();
        _previewService.ClearAllCache();

        return Ok(new { message = "Import completed successfully" });
    }
}
