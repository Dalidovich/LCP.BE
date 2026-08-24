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
    private readonly MediaCache<PreviewResult> _cache;

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
        _cache = new MediaCache<PreviewResult>(settings.Value.PreviewCacheBytes);
    }

    public void InvalidateCache(string videoId)
    {
        _cache.RemoveWhere(k => k.StartsWith(videoId + "_"));
    }

    public void ClearAllCache()
    {
        _cache.Clear();
    }

    public async Task<PreviewResult?> GetPreviewAsync(string videoId, PreviewResolution resolution)
    {
        var cacheKey = $"{videoId}_{resolution}";

        if (_cache.TryGet(cacheKey, out var cached))
            return cached;

        var video = await _repository.GetByIdAsync(videoId);
        if (video is null) return null;

        var videoPath = LibraryPath.Combine(_libraryRootPath, video.RelativePath);
        if (!File.Exists(videoPath)) return null;

        var slices = video.PreviewSlices.Count > 0 ? video.PreviewSlices : PreviewSlice.CalculateSlices(video.Duration);
        var data = await Task.Run(() => GeneratePreview(videoPath, resolution, slices));
        if (data is null) return null;

        var result = new PreviewResult(data, TruncateToSecond(DateTime.UtcNow));

        _cache.Set(cacheKey, result, data.Length);
        return result;
    }

    private static DateTime TruncateToSecond(DateTime value)
    {
        var ticks = value.Ticks;
        return new DateTime(ticks - ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    private byte[]? GeneratePreview(string videoPath, PreviewResolution resolution, List<PreviewSlice> slices)
    {
        return _videoProcessing.GeneratePreview(videoPath, resolution, slices);
    }
}
