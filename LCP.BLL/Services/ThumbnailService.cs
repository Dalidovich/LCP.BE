using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LCP.BLL.Services;

public class ThumbnailService : IThumbnailService
{
    private readonly IVideoRepository _repository;
    private readonly IVideoProcessingService _videoProcessing;
    private readonly string _libraryRootPath;
    private readonly ILogger<ThumbnailService> _logger;
    private readonly MediaCache<ThumbnailResult> _cache;
    private readonly InFlightCoalescer<ThumbnailResult> _inFlight = new();

    public ThumbnailService(
        IVideoRepository repository,
        IVideoProcessingService videoProcessing,
        IOptions<LibrarySettings> settings,
        ILogger<ThumbnailService> logger)
    {
        _repository = repository;
        _videoProcessing = videoProcessing;
        _libraryRootPath = settings.Value.LibraryRootPath;
        _logger = logger;
        _cache = new MediaCache<ThumbnailResult>(settings.Value.ThumbnailCacheBytes);
    }

    public void InvalidateCache(string videoId)
    {
        _cache.Remove(videoId);
    }

    public void ClearAllCache()
    {
        _cache.Clear();
    }

    public Task<ThumbnailResult?> GetThumbnailAsync(string videoId)
    {
        if (_cache.TryGet(videoId, out var cached))
            return Task.FromResult<ThumbnailResult?>(cached);

        return _inFlight.RunAsync(videoId, () => GenerateAndCacheAsync(videoId));
    }

    private async Task<ThumbnailResult?> GenerateAndCacheAsync(string videoId)
    {
        if (_cache.TryGet(videoId, out var cached))
            return cached;

        var video = await _repository.GetByIdAsync(videoId);
        if (video is null) return null;

        var videoPath = LibraryPath.Combine(_libraryRootPath, video.RelativePath);
        if (!File.Exists(videoPath)) return null;

        var data = await Task.Run(() => ExtractFrame(videoPath, video.ThumbnailTimecode));
        if (data is null) return null;

        var result = new ThumbnailResult(data, TruncateToSecond(DateTime.UtcNow));

        _cache.Set(videoId, result, data.Length);
        return result;
    }

    public async Task<ThumbnailResult?> GetThumbnailPreviewAsync(string videoId, double timecode)
    {
        var video = await _repository.GetByIdAsync(videoId);
        if (video is null) return null;

        var videoPath = LibraryPath.Combine(_libraryRootPath, video.RelativePath);
        if (!File.Exists(videoPath)) return null;

        var data = await Task.Run(() => ExtractFrame(videoPath, timecode));
        if (data is null) return null;

        return new ThumbnailResult(data, TruncateToSecond(DateTime.UtcNow));
    }

    private static DateTime TruncateToSecond(DateTime value)
    {
        var ticks = value.Ticks;
        return new DateTime(ticks - ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    private byte[]? ExtractFrame(string videoPath, double timecode)
    {
        return _videoProcessing.ExtractFrame(videoPath, timecode);
    }
}
