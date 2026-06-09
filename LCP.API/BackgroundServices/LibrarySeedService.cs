using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.Extensions.Options;

namespace LCP.API.BackgroundServices;

public class LibrarySeedService : IHostedService
{
    private readonly IVideoRepository _videoRepository;
    private readonly ITagRepository _tagRepository;
    private readonly LibrarySettings _settings;

    public LibrarySeedService(
        IVideoRepository videoRepository,
        ITagRepository tagRepository,
        IOptions<LibrarySettings> settings)
    {
        _videoRepository = videoRepository;
        _tagRepository = tagRepository;
        _settings = settings.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        EnsureSystemFilesDirectory();

        var allEntries = await _videoRepository.GetAllRawAsync();
        if (allEntries.Count == 0)
        {
            var rootPath = _settings.LibraryRootPath;
            if (!string.IsNullOrEmpty(rootPath) && Directory.Exists(rootPath))
            {
                await SeedVideosAsync(rootPath);
            }
        }

        var tags = await _tagRepository.GetAllAsync();
        if (tags.Count == 0)
        {
            var rootPath = _settings.LibraryRootPath;
            if (!string.IsNullOrEmpty(rootPath) && Directory.Exists(rootPath))
            {
                await SeedTagsAsync(rootPath);
            }
        }
    }

    private void EnsureSystemFilesDirectory()
    {
        if (string.IsNullOrEmpty(_settings.LibraryRootPath)) return;

        var systemDir = Path.Combine(_settings.LibraryRootPath, "SYSTEMFILES");
        if (!Directory.Exists(systemDir))
        {
            Directory.CreateDirectory(systemDir);
        }
    }

    private async Task SeedVideosAsync(string rootPath)
    {
        var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv",
            ".flv", ".webm", ".m4v", ".ts"
        };

        var files = Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Where(f => videoExtensions.Contains(Path.GetExtension(f)));

        var videos = new List<VideoMetadata>();
        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(rootPath, file);
            videos.Add(new VideoMetadata
            {
                Id = Guid.NewGuid().ToString(),
                RelativePath = relativePath,
                SystemName = Path.GetFileNameWithoutExtension(file),
                IsDeleted = false
            });
        }

        if (videos.Count != 0)
        {
            await _videoRepository.SaveAllAsync(videos);
        }
    }

    private async Task SeedTagsAsync(string rootPath)
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
            await _tagRepository.AddAsync(tag);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
