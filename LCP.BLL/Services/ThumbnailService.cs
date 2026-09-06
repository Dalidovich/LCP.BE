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
    private sealed record ThumbnailRequest(
        string VideoPath,
        double Timecode,
        string CacheKey,
        string Version,
        DateTime LastModified);

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
        _cache.RemoveWhere(k => k.StartsWith(videoId + "_", StringComparison.Ordinal));
    }

    public void ClearAllCache()
    {
        _cache.Clear();
    }

    public async Task<MediaIdentity?> GetIdentityAsync(string videoId, double? timecode = null)
    {
        var request = await ResolveAsync(videoId, timecode);
        return request is null ? null : new MediaIdentity(request.Version, request.LastModified);
    }

    public async Task<ThumbnailResult?> GetThumbnailAsync(string videoId, double? timecode = null)
    {
        var request = await ResolveAsync(videoId, timecode);
        if (request is null) return null;

        if (_cache.TryGet(request.CacheKey, out var cached))
            return cached;

        return await _inFlight.RunAsync(request.CacheKey, () => GenerateAndCacheAsync(request));
    }

    private async Task<ThumbnailRequest?> ResolveAsync(string videoId, double? timecode)
    {
        var video = await _repository.GetByIdAsync(videoId);
        if (video is null) return null;

        var videoPath = LibraryPath.Combine(_libraryRootPath, video.RelativePath);
        var source = MediaVersion.Probe(videoPath);
        if (source is null) return null;

        var effectiveTimecode = timecode ?? video.ThumbnailTimecode;
        var discriminator = MediaVersion.Format(effectiveTimecode);
        var version = MediaVersion.Compute(source.Value, discriminator);

        return new ThumbnailRequest(
            videoPath,
            effectiveTimecode,
            $"{videoId}_{discriminator}_{version}",
            version,
            source.Value.LastWriteUtc);
    }

    private async Task<ThumbnailResult?> GenerateAndCacheAsync(ThumbnailRequest request)
    {
        if (_cache.TryGet(request.CacheKey, out var cached))
            return cached;

        var data = await Task.Run(() => _videoProcessing.ExtractFrame(request.VideoPath, request.Timecode));
        if (data is null) return null;

        var result = new ThumbnailResult(data, request.LastModified, request.Version);

        _cache.Set(request.CacheKey, result, data.Length);
        return result;
    }
}
