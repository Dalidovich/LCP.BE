using System.Text.Json;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain;
using LCP.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LCP.BLL.Services;

public class LibrarySyncService : ILibrarySyncService
{
    private readonly IVideoRepository _repository;
    private readonly ITagRepository _tagRepository;
    private readonly IProductionInfoRepository _productionInfoRepository;
    private readonly IVideoProcessingService _videoProcessing;
    private readonly ISmartGroupingService _smartGroupingService;
    private readonly LibrarySettings _settings;
    private readonly ILogger<LibrarySyncService> _logger;

    private const string BackupFolderName = "backups";
    private const int BackupRetentionCount = 10;
    private const int DeletionGuardMinimumEntries = 10;

    public LibrarySyncService(
        IVideoRepository repository,
        ITagRepository tagRepository,
        IProductionInfoRepository productionInfoRepository,
        IVideoProcessingService videoProcessing,
        ISmartGroupingService smartGroupingService,
        IOptions<LibrarySettings> settings,
        ILogger<LibrarySyncService> logger)
    {
        _repository = repository;
        _tagRepository = tagRepository;
        _productionInfoRepository = productionInfoRepository;
        _videoProcessing = videoProcessing;
        _smartGroupingService = smartGroupingService;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SyncAsync()
    {
        var rootPath = _settings.LibraryRootPath;
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;

        CreateBackup();

        var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv",
            ".flv", ".webm", ".m4v", ".ts"
        };

        var allEntries = await _repository.GetAllRawAsync();
        var changed = false;

        var filesOnDisk = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Where(f => videoExtensions.Contains(Path.GetExtension(f)))
            .Select(f => LibraryPath.Normalize(Path.GetRelativePath(rootPath, f)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missingEntries = allEntries
            .Where(e => !File.Exists(LibraryPath.Combine(rootPath, e.RelativePath)))
            .ToList();

        if (missingEntries.Count > 0)
        {
            if (IsMassDeletion(missingEntries.Count, allEntries.Count))
            {
                _logger.LogError(
                    "Sync pruning aborted: {MissingCount} of {TotalCount} entries have no file on disk " +
                    "({Ratio:P0}), which exceeds MaxSyncDeletionRatio {Threshold:P0}. " +
                    "Library root '{RootPath}' may be unavailable. Metadata left untouched.",
                    missingEntries.Count, allEntries.Count,
                    (double)missingEntries.Count / allEntries.Count,
                    _settings.MaxSyncDeletionRatio, rootPath);
            }
            else
            {
                var missingSet = missingEntries.ToHashSet();
                allEntries.RemoveAll(missingSet.Contains);
                changed = true;
            }
        }

        foreach (var entry in allEntries)
        {
            if (entry.PreviewSlices.Count == 0)
            {
                entry.PreviewSlices = PreviewSlice.CalculateSlices(entry.Duration);
                changed = true;
            }
        }

        var trackedPaths = allEntries
            .Select(e => LibraryPath.Normalize(e.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in filesOnDisk)
        {
            if (trackedPaths.Contains(relativePath)) continue;

            var fullPath = LibraryPath.Combine(rootPath, relativePath);
            var duration = _videoProcessing.ProbeDuration(fullPath);
            allEntries.Add(new VideoMetadata
            {
                Id = Guid.NewGuid().ToString(),
                RelativePath = relativePath,
                SystemName = Path.GetFileNameWithoutExtension(relativePath),
                Duration = duration,
                PreviewSlices = PreviewSlice.CalculateSlices(duration)
            });
            changed = true;
        }

        if (changed)
        {
            await _repository.SaveAllAsync(allEntries);
        }

        var masterTags = await _tagRepository.GetAllAsync();
        var masterSet = masterTags.Select(t => t.ToLowerInvariant()).ToHashSet();
        var tagChanged = false;
        foreach (var entry in allEntries)
        {
            var removed = entry.Tags.RemoveAll(t => !masterSet.Contains(t.ToLowerInvariant()));
            if (removed > 0) tagChanged = true;
        }
        if (tagChanged)
        {
            await _repository.SaveAllAsync(allEntries);
        }

        var masterStudios = await _productionInfoRepository.GetAllAsync();
        var masterStudioSet = masterStudios.Select(t => t.ToLowerInvariant()).ToHashSet();
        var studioChanged = false;
        foreach (var entry in allEntries)
        {
            var removed = entry.ProductionInfo.RemoveAll(t => !masterStudioSet.Contains(t.ToLowerInvariant()));
            if (removed > 0) studioChanged = true;
        }
        if (studioChanged)
        {
            await _repository.SaveAllAsync(allEntries);
        }

        if (_settings.SmartVideoGrouping)
        {
            await _smartGroupingService.GroupVideosAsync();
        }
    }

    private bool IsMassDeletion(int missingCount, int totalCount)
    {
        if (totalCount < DeletionGuardMinimumEntries) return false;

        var threshold = _settings.MaxSyncDeletionRatio;
        if (threshold <= 0 || threshold >= 1) return false;

        return (double)missingCount / totalCount > threshold;
    }

    private void CreateBackup()
    {
        var sourcePath = _settings.ResolveSystemFilePath(LibrarySettings.JsonFileName);
        if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return;

        if (!HasBackupWorthyContent(sourcePath)) return;

        try
        {
            var backupFolder = Path.Combine(Path.GetDirectoryName(sourcePath)!, BackupFolderName);
            Directory.CreateDirectory(backupFolder);

            var backupPath = Path.Combine(
                backupFolder,
                $"library-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(sourcePath, backupPath, overwrite: true);

            var staleBackups = Directory.EnumerateFiles(backupFolder, "library-*.json")
                .OrderByDescending(f => f, StringComparer.Ordinal)
                .Skip(BackupRetentionCount)
                .ToList();

            foreach (var stale in staleBackups)
            {
                try
                {
                    File.Delete(stale);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete stale library backup '{BackupPath}'", stale);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create library metadata backup from '{SourcePath}'", sourcePath);
        }
    }

    private bool HasBackupWorthyContent(string sourcePath)
    {
        try
        {
            using var stream = File.OpenRead(sourcePath);
            using var document = JsonDocument.Parse(stream);

            if (document.RootElement.ValueKind != JsonValueKind.Array
                || document.RootElement.GetArrayLength() == 0)
            {
                _logger.LogWarning(
                    "Skipping library metadata backup: '{SourcePath}' is empty", sourcePath);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Skipping library metadata backup: '{SourcePath}' is unreadable or malformed", sourcePath);
            return false;
        }
    }
}
