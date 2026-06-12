using System.Diagnostics;
using System.Text.RegularExpressions;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.Extensions.Options;
using NReco.VideoConverter;

namespace LCP.API.BackgroundServices;

public class LibrarySeedService : IHostedService
{
    private readonly IVideoRepository _videoRepository;
    private readonly ITagRepository _tagRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ISmartGroupingService _smartGroupingService;
    private readonly LibrarySettings _settings;

    public LibrarySeedService(
        IVideoRepository videoRepository,
        ITagRepository tagRepository,
        ISettingsRepository settingsRepository,
        ISmartGroupingService smartGroupingService,
        IOptions<LibrarySettings> settings)
    {
        _videoRepository = videoRepository;
        _tagRepository = tagRepository;
        _settingsRepository = settingsRepository;
        _smartGroupingService = smartGroupingService;
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

        var settingsFilePath = _settings.ResolveSystemFilePath(_settings.SettingsFilePath);
        if (!File.Exists(settingsFilePath))
        {
            await _settingsRepository.UpdateAsync(new SiteSettings());
        }

        if (_settings.SmartVideoGrouping)
        {
            await _smartGroupingService.GroupVideosAsync();
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
            var duration = ProbeDuration(file);
            videos.Add(new VideoMetadata
            {
                Id = Guid.NewGuid().ToString(),
                RelativePath = relativePath,
                SystemName = Path.GetFileNameWithoutExtension(file),
                IsDeleted = false,
                Duration = duration,
                PreviewSlices = PreviewSlice.CalculateSlices(duration)
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

    private static double ProbeDuration(string videoPath)
    {
        try
        {
            var probe = new FFMpegConverter();
            probe.ExtractFFmpeg();

            var ffmpegPath = Path.Combine(probe.FFMpegToolPath, probe.FFMpegExeName);
            if (!File.Exists(ffmpegPath)) return 0;

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-i \"{videoPath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return 0;

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(3000);

            var match = Regex.Match(stderr, @"Duration: (\d+):(\d+):(\d+)\.(\d+)");
            if (match.Success)
            {
                var h = int.Parse(match.Groups[1].Value);
                var m = int.Parse(match.Groups[2].Value);
                var s = int.Parse(match.Groups[3].Value);
                var ms = int.Parse(match.Groups[4].Value.PadRight(3, '0')[..3]);
                return new TimeSpan(0, h, m, s, ms).TotalSeconds;
            }
        }
        catch { }
        return 0;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
