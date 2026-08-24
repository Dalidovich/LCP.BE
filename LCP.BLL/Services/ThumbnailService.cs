using System.Collections.Concurrent;
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
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();
    private const int MaxCacheSize = 100;

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
    }

    public void InvalidateCache(string videoId)
    {
        Cache.TryRemove(videoId, out _);
    }

    public void ClearAllCache()
    {
        Cache.Clear();
    }

    public async Task<ThumbnailResult?> GetThumbnailAsync(string videoId)
    {
        if (Cache.TryGetValue(videoId, out var cached))
            return new ThumbnailResult(cached, DateTime.UtcNow);

        var video = await _repository.GetByIdAsync(videoId);
        if (video is null) return null;

        var videoPath = LibraryPath.Combine(_libraryRootPath, video.RelativePath);
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

        var videoPath = LibraryPath.Combine(_libraryRootPath, video.RelativePath);
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

    private byte[]? ExtractFrame(string videoPath, double timecode)
    {
        return _videoProcessing.ExtractFrame(videoPath, timecode);
    }
}
