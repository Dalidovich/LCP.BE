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
    private sealed record PreviewRequest(
        string VideoPath,
        PreviewResolution Resolution,
        List<PreviewSlice> Slices,
        string CacheKey,
        string Version,
        DateTime LastModified);

    private readonly IVideoRepository _repository;
    private readonly IVideoProcessingService _videoProcessing;
    private readonly string _libraryRootPath;
    private readonly ILogger<PreviewService> _logger;
    private readonly MediaCache<PreviewResult> _cache;
    private readonly InFlightCoalescer<PreviewResult> _inFlight = new();

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
        _cache.RemoveWhere(k => k.StartsWith(videoId + "_", StringComparison.Ordinal));
    }

    public void ClearAllCache()
    {
        _cache.Clear();
    }

    public async Task<MediaIdentity?> GetIdentityAsync(string videoId, PreviewResolution resolution)
    {
        var request = await ResolveAsync(videoId, resolution);
        return request is null ? null : new MediaIdentity(request.Version, request.LastModified);
    }

    public async Task<PreviewResult?> GetPreviewAsync(string videoId, PreviewResolution resolution)
    {
        var request = await ResolveAsync(videoId, resolution);
        if (request is null) return null;

        if (_cache.TryGet(request.CacheKey, out var cached))
            return cached;

        return await _inFlight.RunAsync(request.CacheKey, () => GenerateAndCacheAsync(request));
    }

    private async Task<PreviewRequest?> ResolveAsync(string videoId, PreviewResolution resolution)
    {
        var video = await _repository.GetByIdAsync(videoId);
        if (video is null) return null;

        var videoPath = LibraryPath.Combine(_libraryRootPath, video.RelativePath);
        var source = MediaVersion.Probe(videoPath);
        if (source is null) return null;

        var slices = video.PreviewSlices.Count > 0
            ? video.PreviewSlices
            : PreviewSlice.CalculateSlices(video.Duration);

        var discriminator = $"{resolution}|{DescribeSlices(slices)}";
        var version = MediaVersion.Compute(source.Value, discriminator);

        return new PreviewRequest(
            videoPath,
            resolution,
            slices,
            $"{videoId}_{resolution}_{version}",
            version,
            source.Value.LastWriteUtc);
    }

    private static string DescribeSlices(List<PreviewSlice> slices)
    {
        return string.Join(',', slices.Select(s =>
            $"{MediaVersion.Format(s.Start)}-{MediaVersion.Format(s.Duration)}"));
    }

    private async Task<PreviewResult?> GenerateAndCacheAsync(PreviewRequest request)
    {
        if (_cache.TryGet(request.CacheKey, out var cached))
            return cached;

        var data = await Task.Run(() =>
            _videoProcessing.GeneratePreview(request.VideoPath, request.Resolution, request.Slices));
        if (data is null) return null;

        var result = new PreviewResult(data, request.LastModified, request.Version);

        _cache.Set(request.CacheKey, result, data.Length);
        return result;
    }
}
