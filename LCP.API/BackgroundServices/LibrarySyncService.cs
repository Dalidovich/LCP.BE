using LCP.BLL.Helpers;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.Extensions.Options;

namespace LCP.API.BackgroundServices;

public class LibrarySyncService : IHostedService
{
    private readonly IVideoRepository _repository;
    private readonly ITagRepository _tagRepository;
    private readonly ISmartGroupingService _smartGroupingService;
    private readonly LibrarySettings _settings;

    public LibrarySyncService(IVideoRepository repository, ITagRepository tagRepository, ISmartGroupingService smartGroupingService, IOptions<LibrarySettings> settings)
    {
        _repository = repository;
        _tagRepository = tagRepository;
        _smartGroupingService = smartGroupingService;
        _settings = settings.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var rootPath = _settings.LibraryRootPath;
        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath)) return;

        var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv",
            ".flv", ".webm", ".m4v", ".ts"
        };

        var allEntries = await _repository.GetAllRawAsync();
        var changed = false;

        var filesOnDisk = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Where(f => videoExtensions.Contains(Path.GetExtension(f)))
            .Select(f => Path.GetRelativePath(rootPath, f))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removedCount = allEntries.RemoveAll(e => !File.Exists(Path.Combine(rootPath, e.RelativePath)));
        if (removedCount > 0) changed = true;

        foreach (var entry in allEntries)
        {
            if (entry.PreviewSlices.Count == 0)
            {
                entry.PreviewSlices = PreviewSlice.CalculateSlices(entry.Duration);
                changed = true;
            }
        }

        var trackedPaths = allEntries
            .Select(e => e.RelativePath.Replace('/', '\\'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in filesOnDisk)
        {
            if (trackedPaths.Contains(relativePath.Replace('/', '\\'))) continue;

            var fullPath = Path.Combine(rootPath, relativePath);
            var duration = FFProbeHelper.ProbeDuration(fullPath);
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

        if (_settings.SmartVideoGrouping)
        {
            await _smartGroupingService.GroupVideosAsync();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
