using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain;
using LCP.Domain.Entities;
using Microsoft.Extensions.Options;

namespace LCP.API.BackgroundServices;

public class LibraryStartupService : BackgroundService
{
    private readonly IVideoRepository _videoRepository;
    private readonly ITagRepository _tagRepository;
    private readonly IProductionInfoRepository _productionInfoRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ISmartGroupingService _smartGroupingService;
    private readonly IVideoProcessingService _videoProcessing;
    private readonly ILibrarySyncService _syncService;
    private readonly ILogger<LibraryStartupService> _logger;
    private readonly LibrarySettings _settings;

    public LibraryStartupService(
        IVideoRepository videoRepository,
        ITagRepository tagRepository,
        IProductionInfoRepository productionInfoRepository,
        ISettingsRepository settingsRepository,
        ISmartGroupingService smartGroupingService,
        IVideoProcessingService videoProcessing,
        ILibrarySyncService syncService,
        ILogger<LibraryStartupService> logger,
        IOptions<LibrarySettings> settings)
    {
        _videoRepository = videoRepository;
        _tagRepository = tagRepository;
        _productionInfoRepository = productionInfoRepository;
        _settingsRepository = settingsRepository;
        _smartGroupingService = smartGroupingService;
        _videoProcessing = videoProcessing;
        _syncService = syncService;
        _logger = logger;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        try
        {
            await SeedAsync(stoppingToken);

            stoppingToken.ThrowIfCancellationRequested();

            _logger.LogInformation("Library sync started");
            await _syncService.SyncAsync();
            _logger.LogInformation("Library sync completed");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Library startup indexing cancelled before completion");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library startup indexing failed; the application stays available");
        }
    }

    private async Task SeedAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Library seeding started");

        if (!EnsureSystemFilesDirectory())
        {
            _logger.LogWarning("LibraryRootPath is not configured; skipping library seeding");
            return;
        }

        var allEntries = await _videoRepository.GetAllRawAsync();
        if (allEntries.Count == 0)
        {
            var rootPath = _settings.LibraryRootPath;
            if (!string.IsNullOrEmpty(rootPath) && Directory.Exists(rootPath))
            {
                await SeedVideosAsync(rootPath, stoppingToken);
            }
            else
            {
                _logger.LogWarning("Library root path {RootPath} does not exist; skipping video seeding", rootPath);
            }
        }
        else
        {
            var tags = await _tagRepository.GetAllAsync();
            if (tags.Count == 0)
            {
                await SeedTagsAsync(stoppingToken);
            }

            var studios = await _productionInfoRepository.GetAllAsync();
            if (studios.Count == 0)
            {
                await SeedProductionInfoAsync(stoppingToken);
            }
        }

        stoppingToken.ThrowIfCancellationRequested();

        var settingsFilePath = _settings.ResolveSystemFilePath(LibrarySettings.SettingsFileName);
        if (!File.Exists(settingsFilePath))
        {
            await _settingsRepository.UpdateAsync(new SiteSettings());
        }

        if (_settings.SmartVideoGrouping)
        {
            stoppingToken.ThrowIfCancellationRequested();
            await _smartGroupingService.GroupVideosAsync();
        }

        _logger.LogInformation("Library seeding completed");
    }

    private bool EnsureSystemFilesDirectory()
    {
        if (string.IsNullOrEmpty(_settings.LibraryRootPath)) return false;

        var systemDir = Path.Combine(_settings.LibraryRootPath, "SYSTEMFILES");
        if (!Directory.Exists(systemDir))
        {
            Directory.CreateDirectory(systemDir);
        }
        return true;
    }

    private async Task SeedVideosAsync(string rootPath, CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scanning {RootPath} for video files", rootPath);

        var files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Where(f => VideoFileExtensions.Supported.Contains(Path.GetExtension(f)));

        var videos = new List<VideoMetadata>();
        foreach (var file in files)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var relativePath = LibraryPath.Normalize(Path.GetRelativePath(rootPath, file));
            var duration = _videoProcessing.ProbeDuration(file);
            videos.Add(new VideoMetadata
            {
                Id = Guid.NewGuid().ToString(),
                RelativePath = relativePath,
                SystemName = Path.GetFileNameWithoutExtension(file),
                Duration = duration,
                PreviewSlices = PreviewSlice.CalculateSlices(duration),
                ThumbnailTimecode = 2 > duration ? duration : 2
            });

            if (videos.Count % 100 == 0)
            {
                _logger.LogInformation("Indexed {Count} video files so far", videos.Count);
            }
        }

        if (videos.Count != 0)
        {
            await _videoRepository.SaveAllAsync(videos);
        }

        _logger.LogInformation("Indexed {Count} video files", videos.Count);
    }

    private async Task SeedTagsAsync(CancellationToken stoppingToken)
    {
        var allEntries = await _videoRepository.GetAllRawAsync();
        var tags = allEntries
            .SelectMany(v => v.Tags)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();

        foreach (var tag in tags)
        {
            stoppingToken.ThrowIfCancellationRequested();
            await _tagRepository.AddAsync(tag);
        }

        _logger.LogInformation("Seeded {Count} tags", tags.Count);
    }

    private async Task SeedProductionInfoAsync(CancellationToken stoppingToken)
    {
        var allEntries = await _videoRepository.GetAllRawAsync();
        var studios = allEntries
            .SelectMany(v => v.ProductionInfo)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();

        foreach (var studio in studios)
        {
            stoppingToken.ThrowIfCancellationRequested();
            await _productionInfoRepository.AddAsync(studio);
        }

        _logger.LogInformation("Seeded {Count} production info entries", studios.Count);
    }
}
