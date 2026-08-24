using System.Collections.Concurrent;
using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain;
using LCP.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LCP.BLL.Services;

public class PreviewService : IPreviewService
{
    private readonly IVideoRepository _repository;
    private readonly IVideoProcessingService _videoProcessing;
    private readonly string _libraryRootPath;
    private readonly ILogger<PreviewService> _logger;
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();
    private const int MaxCacheSize = 100;

    public PreviewService(
        IVideoRepository repository,
        IVideoProcessingService videoProcessing,
        IOptions<LibrarySettings> settings,
        ILogger<PreviewService> logger)
    {
        _repository = repository;
        _videoProcessing = videoProcessing;
        _libraryRootPath = settings.Value.LibraryRootPath;
        _logger = logger;
    }

    public void InvalidateCache(string videoId)
    {
        var keys = Cache.Keys.Where(k => k.StartsWith(videoId + "_")).ToArray();
        foreach (var key in keys)
            Cache.TryRemove(key, out _);
    }

    public void ClearAllCache()
    {
        Cache.Clear();
    }

    public async Task<PreviewResult?> GetPreviewAsync(string videoId, PreviewResolution resolution)
    {
        var cacheKey = $"{videoId}_{resolution}";

        if (Cache.TryGetValue(cacheKey, out var cached))
            return new PreviewResult(cached, DateTime.UtcNow);

        var video = await _repository.GetByIdAsync(videoId);
        if (video is null) return null;

        var videoPath = LibraryPath.Combine(_libraryRootPath, video.RelativePath);
        if (!File.Exists(videoPath)) return null;

        var slices = video.PreviewSlices.Count > 0 ? video.PreviewSlices : PreviewSlice.CalculateSlices(video.Duration);
        var data = await Task.Run(() => GeneratePreview(videoPath, resolution, slices));
        if (data is null) return null;

        EvictIfNeeded();
        Cache[cacheKey] = data;
        return new PreviewResult(data, DateTime.UtcNow);
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

    private byte[]? GeneratePreview(string videoPath, PreviewResolution resolution, List<PreviewSlice> slices)
    {
        return _videoProcessing.GeneratePreview(videoPath, resolution, slices);
    }
}
