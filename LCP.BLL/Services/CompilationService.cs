using LCP.BLL.DTOs;
using LCP.BLL.Helpers;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain;
using LCP.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LCP.BLL.Services;

public class CompilationService : ICompilationService
{
    private const double AnimeSpeed = 2.0;

    private sealed record StoredCompilation(string Fingerprint, CompilationDto Info, byte[] Data);

    private readonly IVideoRepository _videoRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly IWatchRecordRepository _watchRecordRepository;
    private readonly IVideoProcessingService _videoProcessing;
    private readonly LibrarySettings _settings;
    private readonly ILogger<CompilationService> _logger;
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private StoredCompilation? _current;

    public CompilationService(
        IVideoRepository videoRepository,
        ISettingsRepository settingsRepository,
        IWatchRecordRepository watchRecordRepository,
        IVideoProcessingService videoProcessing,
        IOptions<LibrarySettings> settings,
        ILogger<CompilationService> logger)
    {
        _videoRepository = videoRepository;
        _settingsRepository = settingsRepository;
        _watchRecordRepository = watchRecordRepository;
        _videoProcessing = videoProcessing;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<CompilationDto?> BuildAsync(bool rebuild)
    {
        await _buildLock.WaitAsync();
        try
        {
            var siteSettings = await _settingsRepository.GetAsync();
            var videos = EligibleVideos(await _videoRepository.GetSnapshotAsync(), siteSettings);
            var records = await _watchRecordRepository.GetAllAsync();
            var durations = videos.ToDictionary(p => p.Key, p => p.Value.Duration, StringComparer.Ordinal);

            var plan = CompilationPlanner.Plan(records, durations, _settings.Compilation);
            if (plan.Count == 0) return null;

            var animeSpeedUp = siteSettings?.AnimeSpeedUp ?? false;
            var speeds = videos.ToDictionary(p => p.Key, p => SpeedOf(p.Value, animeSpeedUp), StringComparer.Ordinal);

            var fingerprint = string.Join(';', plan
                .OrderBy(m => m.VideoId, StringComparer.Ordinal)
                .ThenBy(m => m.Start)
                .Select(m => $"{m.VideoId}:{m.Start}:{m.Duration}:{speeds[m.VideoId]}"));

            if (!rebuild && _current?.Fingerprint == fingerprint)
                return _current.Info;

            var ordered = plan.ToArray();
            Random.Shared.Shuffle(ordered);

            var clips = ordered
                .Select(m => new CompilationClip(ResolvePath(videos[m.VideoId]), m.Start, m.Duration, speeds[m.VideoId]))
                .ToList();

            _logger.LogInformation("Building a compilation of {Count} moments ({Seconds}s)",
                ordered.Length, ordered.Sum(m => m.Duration));

            var data = await Task.Run(() => _videoProcessing.GenerateCompilation(
                clips, _settings.Compilation.Width, _settings.Compilation.Height));
            if (data is null)
                throw new InvalidOperationException("Failed to generate the compilation");

            var moments = ToMoments(ordered, videos, speeds);
            var info = new CompilationDto
            {
                Id = Guid.NewGuid().ToString("N"),
                Duration = moments.Sum(m => m.Duration / m.Speed),
                Moments = moments
            };

            _current = new StoredCompilation(fingerprint, info, data);
            return info;
        }
        finally
        {
            _buildLock.Release();
        }
    }

    public byte[]? GetData(string id)
    {
        var current = _current;
        return current is not null && current.Info.Id == id ? current.Data : null;
    }

    private static double SpeedOf(VideoMetadata video, bool animeSpeedUp)
    {
        return animeSpeedUp && video.Type == VideoType.Anime ? AnimeSpeed : 1.0;
    }

    private Dictionary<string, VideoMetadata> EligibleVideos(
        IEnumerable<VideoMetadata> snapshot,
        SiteSettings? siteSettings)
    {
        var typeFilter = siteSettings?.VideoTypeFilter ?? [];

        return snapshot
            .Where(v => typeFilter.Count == 0 || typeFilter.Contains(v.Type))
            .Where(v => File.Exists(ResolvePath(v)))
            .ToDictionary(v => v.Id, StringComparer.Ordinal);
    }

    private string ResolvePath(VideoMetadata video)
    {
        return LibraryPath.Combine(_settings.LibraryRootPath, video.RelativePath);
    }

    private static List<CompilationMomentDto> ToMoments(
        IEnumerable<SelectedMoment> ordered,
        IReadOnlyDictionary<string, VideoMetadata> videos,
        IReadOnlyDictionary<string, double> speeds)
    {
        var moments = new List<CompilationMomentDto>();
        var offset = 0.0;

        foreach (var moment in ordered)
        {
            var video = videos[moment.VideoId];
            var speed = speeds[moment.VideoId];
            moments.Add(new CompilationMomentDto
            {
                VideoId = moment.VideoId,
                NameEn = string.IsNullOrEmpty(video.NameEn) ? video.SystemName : video.NameEn,
                Offset = offset,
                Start = moment.Start,
                Duration = moment.Duration,
                Speed = speed
            });
            offset += moment.Duration / speed;
        }

        return moments;
    }
}
