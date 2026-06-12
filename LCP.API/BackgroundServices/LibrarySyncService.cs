using System.Diagnostics;
using System.Text.RegularExpressions;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.Extensions.Options;
using NReco.VideoConverter;

namespace LCP.API.BackgroundServices;

public class LibrarySyncService : IHostedService
{
    private readonly IVideoRepository _repository;
    private readonly ISmartGroupingService _smartGroupingService;
    private readonly LibrarySettings _settings;

    public LibrarySyncService(IVideoRepository repository, ISmartGroupingService smartGroupingService, IOptions<LibrarySettings> settings)
    {
        _repository = repository;
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

        var removedCount = allEntries.RemoveAll(e =>
        {
            var fullPath = Path.Combine(rootPath, e.RelativePath);
            return !File.Exists(fullPath);
        });
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
            var duration = ProbeDuration(fullPath);
            allEntries.Add(new VideoMetadata
            {
                Id = Guid.NewGuid().ToString(),
                RelativePath = relativePath,
                SystemName = Path.GetFileNameWithoutExtension(relativePath),
                IsDeleted = false,
                Duration = duration,
                PreviewSlices = PreviewSlice.CalculateSlices(duration)
            });
            changed = true;
        }

        if (changed)
        {
            await _repository.SaveAllAsync(allEntries);
        }

        if (_settings.SmartVideoGrouping)
        {
            await _smartGroupingService.GroupVideosAsync();
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
