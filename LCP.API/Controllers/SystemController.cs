using System.IO.Compression;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain;
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
    private readonly ILogger<SystemController> _logger;
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
        IOptions<LibrarySettings> settings,
        ILogger<SystemController> logger)
    {
        _lifetime = lifetime;
        _videoService = videoService;
        _videoRepository = videoRepository;
        _tagRepository = tagRepository;
        _settingsRepository = settingsRepository;
        _productionInfoRepository = productionInfoRepository;
        _thumbnailService = thumbnailService;
        _previewService = previewService;
        _logger = logger;
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
            var path = LibraryPath.Combine(_libraryRootPath, v.RelativePath);
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
            var videoPath = LibraryPath.Combine(_libraryRootPath, video.RelativePath);
            if (!System.IO.File.Exists(videoPath)) continue;

            var entry = archive.CreateEntry(LibraryPath.ToArchiveEntryName(video.RelativePath), CompressionLevel.NoCompression);
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

        var libraryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_libraryRootPath));
        var parentDir = Path.GetDirectoryName(libraryRoot);

        if (string.IsNullOrEmpty(parentDir))
            return BadRequest(new { error = "Library root path has no parent directory to stage the import in" });

        var stagingPath = Path.Combine(parentDir, $".lcp-import-{Guid.NewGuid():N}");
        var oldLibraryPath = Path.Combine(parentDir, $".lcp-old-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingPath);

            try
            {
                using var archive = new ZipArchive(file.OpenReadStream(), ZipArchiveMode.Read);

                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    var entryPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);

                    if (!TryResolveEntryPath(stagingPath, entryPath, out var fullPath))
                    {
                        _logger.LogWarning("Skipped archive entry outside library root: {EntryName}", entry.FullName);
                        continue;
                    }

                    var entryDir = Path.GetDirectoryName(fullPath);

                    if (!string.IsNullOrEmpty(entryDir) && !Directory.Exists(entryDir))
                        Directory.CreateDirectory(entryDir);

                    entry.ExtractToFile(fullPath, overwrite: true);
                }
            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning(ex, "Rejected import: uploaded file is not a valid ZIP archive");
                return BadRequest(new { error = "Uploaded file is not a valid ZIP archive" });
            }

            var systemFiles = new[]
            {
                LibrarySettings.JsonFileName,
                LibrarySettings.TagsFileName,
                LibrarySettings.SettingsFileName,
                LibrarySettings.ProductionInfoFileName,
            };

            foreach (var sysFile in systemFiles)
            {
                var filePath = Path.Combine(stagingPath, "SYSTEMFILES", sysFile);
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

            ct.ThrowIfCancellationRequested();

            var hadExistingLibrary = Directory.Exists(libraryRoot);

            if (hadExistingLibrary)
                Directory.Move(libraryRoot, oldLibraryPath);

            try
            {
                Directory.Move(stagingPath, libraryRoot);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to move staged import into {LibraryRoot}, restoring previous library", libraryRoot);

                if (hadExistingLibrary)
                    Directory.Move(oldLibraryPath, libraryRoot);

                throw;
            }

            if (hadExistingLibrary)
                TryDeleteDirectory(oldLibraryPath);
        }
        finally
        {
            TryDeleteDirectory(stagingPath);
        }

        await _videoRepository.InvalidateCacheAsync();
        await _tagRepository.InvalidateCacheAsync();
        await _settingsRepository.InvalidateCacheAsync();
        await _productionInfoRepository.InvalidateCacheAsync();
        _thumbnailService.ClearAllCache();
        _previewService.ClearAllCache();

        return Ok(new { message = "Import completed successfully" });
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete directory {Path}", path);
        }
    }

    private static bool TryResolveEntryPath(string root, string entryPath, out string fullPath)
    {
        fullPath = string.Empty;

        string resolved;
        try
        {
            resolved = Path.GetFullPath(Path.Combine(root, entryPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        fullPath = resolved;
        return true;
    }
}
