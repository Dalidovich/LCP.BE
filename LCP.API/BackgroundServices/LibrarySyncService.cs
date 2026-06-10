using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.Extensions.Options;

namespace LCP.API.BackgroundServices;

public class LibrarySyncService : IHostedService
{
    private readonly IVideoRepository _repository;
    private readonly LibrarySettings _settings;

    public LibrarySyncService(IVideoRepository repository, IOptions<LibrarySettings> settings)
    {
        _repository = repository;
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

        var removedCount = allEntries.RemoveAll(e =>
        {
            var fullPath = Path.Combine(rootPath, e.RelativePath);
            return !File.Exists(fullPath);
        });
        if (removedCount > 0) changed = true;

        var trackedPaths = allEntries
            .Select(e => e.RelativePath.Replace('/', '\\'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in filesOnDisk)
        {
            if (trackedPaths.Contains(relativePath.Replace('/', '\\'))) continue;

            allEntries.Add(new VideoMetadata
            {
                Id = Guid.NewGuid().ToString(),
                RelativePath = relativePath,
                SystemName = Path.GetFileNameWithoutExtension(relativePath),
                IsDeleted = false
            });
            changed = true;
        }

        if (changed)
        {
            await _repository.SaveAllAsync(allEntries);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
