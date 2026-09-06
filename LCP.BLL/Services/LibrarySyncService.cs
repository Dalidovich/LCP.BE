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
    private const double RelocationDurationToleranceSeconds = 1;

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

        var filesOnDisk = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Where(f => VideoFileExtensions.Supported.Contains(Path.GetExtension(f)))
            .Select(f => LibraryPath.Normalize(Path.GetRelativePath(rootPath, f)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var snapshot = await _repository.GetAllRawAsync();
        var knownPaths = snapshot
            .Select(e => LibraryPath.Normalize(e.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var probedDurations = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var relativePath in filesOnDisk.Where(p => !knownPaths.Contains(p)))
        {
            probedDurations[relativePath] = _videoProcessing.ProbeDuration(
                LibraryPath.Combine(rootPath, relativePath));
        }

        var masterTags = await _tagRepository.GetAllAsync();
        var masterTagSet = masterTags.Select(t => t.ToLowerInvariant()).ToHashSet();

        var masterStudios = await _productionInfoRepository.GetAllAsync();
        var masterStudioSet = masterStudios.Select(t => t.ToLowerInvariant()).ToHashSet();

        await _repository.MutateAsync<object?>(allEntries =>
        {
            var changed = false;

            var missingEntries = allEntries
                .Where(e => !File.Exists(LibraryPath.Combine(rootPath, e.RelativePath)))
                .ToList();

            var untrackedPaths = filesOnDisk.Where(p => !knownPaths.Contains(p)).ToList();
            var relocations = MatchRelocations(missingEntries, untrackedPaths, probedDurations);

            if (relocations.Count > 0)
            {
                foreach (var relocation in relocations)
                {
                    _logger.LogInformation(
                        "Video file moved from '{OldPath}' to '{NewPath}'; keeping metadata of entry {VideoId}",
                        relocation.Entry.RelativePath, relocation.NewPath, relocation.Entry.Id);

                    relocation.Entry.RelativePath = relocation.NewPath;
                }

                var relocatedEntries = relocations.Select(r => r.Entry).ToHashSet();
                missingEntries.RemoveAll(relocatedEntries.Contains);
                changed = true;
            }

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
                if (entry.PreviewSlices.Count == 0
                    || !PreviewSlice.AreWithinBounds(entry.PreviewSlices, entry.Duration))
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

                if (!probedDurations.TryGetValue(relativePath, out var duration))
                {
                    duration = _videoProcessing.ProbeDuration(
                        LibraryPath.Combine(rootPath, relativePath));
                }

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

            foreach (var entry in allEntries)
            {
                var removedTags = entry.Tags.RemoveAll(t => !masterTagSet.Contains(t.ToLowerInvariant()));
                if (removedTags > 0) changed = true;

                var removedStudios = entry.ProductionInfo.RemoveAll(t => !masterStudioSet.Contains(t.ToLowerInvariant()));
                if (removedStudios > 0) changed = true;
            }

            return (changed, null);
        });

        if (_settings.SmartVideoGrouping)
        {
            await _smartGroupingService.GroupVideosAsync();
        }
    }

    private static List<Relocation> MatchRelocations(
        IReadOnlyList<VideoMetadata> missingEntries,
        IReadOnlyList<string> untrackedPaths,
        IReadOnlyDictionary<string, double> probedDurations)
    {
        var relocations = new List<Relocation>();
        if (missingEntries.Count == 0 || untrackedPaths.Count == 0) return relocations;

        var pathsByFileName = untrackedPaths.ToLookup(
            p => Path.GetFileName(LibraryPath.Normalize(p)),
            StringComparer.OrdinalIgnoreCase);

        var entriesByFileName = missingEntries.GroupBy(
            e => Path.GetFileName(LibraryPath.Normalize(e.RelativePath)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in entriesByFileName)
        {
            var candidatePaths = pathsByFileName[group.Key].ToList();
            if (candidatePaths.Count == 0) continue;

            var candidateEntries = group.ToList();
            var matched = MatchByDuration(candidateEntries, candidatePaths, probedDurations);
            relocations.AddRange(matched);

            var remainingEntries = candidateEntries
                .Where(e => matched.All(m => !ReferenceEquals(m.Entry, e)))
                .ToList();
            var remainingPaths = candidatePaths
                .Where(p => matched.All(m => !string.Equals(m.NewPath, p, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (remainingEntries.Count == 1
                && remainingPaths.Count == 1
                && !DurationsConflict(remainingEntries[0].Duration, remainingPaths[0], probedDurations))
            {
                relocations.Add(new Relocation(remainingEntries[0], remainingPaths[0]));
            }
        }

        return relocations;
    }

    private static List<Relocation> MatchByDuration(
        List<VideoMetadata> candidateEntries,
        List<string> candidatePaths,
        IReadOnlyDictionary<string, double> probedDurations)
    {
        var matched = new List<Relocation>();

        foreach (var entry in candidateEntries)
        {
            if (entry.Duration <= 0) continue;

            var matchingPaths = candidatePaths
                .Where(p => DurationsMatch(entry.Duration, p, probedDurations))
                .ToList();
            if (matchingPaths.Count != 1) continue;

            var competingEntries = candidateEntries
                .Count(e => DurationsMatch(e.Duration, matchingPaths[0], probedDurations));
            if (competingEntries != 1) continue;

            matched.Add(new Relocation(entry, matchingPaths[0]));
        }

        return matched;
    }

    private static bool DurationsMatch(
        double entryDuration,
        string path,
        IReadOnlyDictionary<string, double> probedDurations)
    {
        if (entryDuration <= 0) return false;
        if (!probedDurations.TryGetValue(path, out var probed) || probed <= 0) return false;

        return Math.Abs(probed - entryDuration) <= RelocationDurationToleranceSeconds;
    }

    private static bool DurationsConflict(
        double entryDuration,
        string path,
        IReadOnlyDictionary<string, double> probedDurations)
    {
        if (entryDuration <= 0) return false;
        if (!probedDurations.TryGetValue(path, out var probed) || probed <= 0) return false;

        return Math.Abs(probed - entryDuration) > RelocationDurationToleranceSeconds;
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

    private sealed record Relocation(VideoMetadata Entry, string NewPath);
}
