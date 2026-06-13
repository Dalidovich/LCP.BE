using System.Collections.Concurrent;
using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NReco.VideoConverter;

namespace LCP.BLL.Services;

public class ThumbnailService : IThumbnailService
{
    private readonly IVideoRepository _repository;
    private readonly string _libraryRootPath;
    private readonly ILogger<ThumbnailService> _logger;
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();
    private const int MaxCacheSize = 100;

    public ThumbnailService(
        IVideoRepository repository,
        IOptions<LibrarySettings> settings,
        ILogger<ThumbnailService> logger)
    {
        _repository = repository;
        _libraryRootPath = settings.Value.LibraryRootPath;
        _logger = logger;
    }

    public void InvalidateCache(string videoId)
    {
        Cache.TryRemove(videoId, out _);
    }

    public async Task<ThumbnailResult?> GetThumbnailAsync(string videoId)
    {
        if (Cache.TryGetValue(videoId, out var cached))
            return new ThumbnailResult(cached, DateTime.UtcNow);

        var video = await _repository.GetByIdAsync(videoId);
        if (video is null) return null;

        var videoPath = Path.Combine(_libraryRootPath, video.RelativePath);
        if (!File.Exists(videoPath)) return null;

        var data = await Task.Run(() => ExtractFrame(videoPath, video.ThumbnailTimecode));
        if (data is null) return null;

        EvictIfNeeded();
        Cache[videoId] = data;
        return new ThumbnailResult(data, DateTime.UtcNow);
    }

    public async Task<ThumbnailResult?> GetThumbnailPreviewAsync(string videoId, double timecode)
    {
        var video = await _repository.GetByIdAsync(videoId);
        if (video is null) return null;

        var videoPath = Path.Combine(_libraryRootPath, video.RelativePath);
        if (!File.Exists(videoPath)) return null;

        var data = await Task.Run(() => ExtractFrame(videoPath, timecode));
        if (data is null) return null;

        return new ThumbnailResult(data, DateTime.UtcNow);
    }

    private static void EvictIfNeeded()
    {
        if (Cache.Count >= MaxCacheSize)
        {
            var key = Cache.Keys.FirstOrDefault();
            if (key is not null)
                Cache.TryRemove(key, out _);
        }
    }

    private byte[]? ExtractFrame(string videoPath, double thumbnailTimecode)
    {
        try
        {
            var ffmpeg = new FFMpegConverter();

            using var ms = new MemoryStream();

            float? frameTime = thumbnailTimecode >= 0
                ? (float)thumbnailTimecode
                : 1f;

            _logger.LogInformation("Generating thumbnail for {VideoPath} at {Seek}s", videoPath, frameTime);

            ffmpeg.GetVideoThumbnail(videoPath, ms, frameTime);
            return ms.Length > 0 ? ms.ToArray() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate thumbnail for {VideoPath}", videoPath);
            return null;
        }
    }
}
