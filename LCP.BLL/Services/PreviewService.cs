using System.Collections.Concurrent;
using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NReco.VideoConverter;

namespace LCP.BLL.Services;

public class PreviewService : IPreviewService
{
    private readonly IVideoRepository _repository;
    private readonly string _libraryRootPath;
    private readonly ILogger<PreviewService> _logger;
    private static readonly ConcurrentDictionary<string, byte[]> Cache = new();

    public PreviewService(
        IVideoRepository repository,
        IOptions<LibrarySettings> settings,
        ILogger<PreviewService> logger)
    {
        _repository = repository;
        _libraryRootPath = settings.Value.LibraryRootPath;
        _logger = logger;
    }

    public void InvalidateCache(string videoId)
    {
        var keys = Cache.Keys.Where(k => k.StartsWith(videoId + "_")).ToArray();
        foreach (var key in keys)
            Cache.TryRemove(key, out _);
    }

    public async Task<PreviewResult?> GetPreviewAsync(string videoId, PreviewResolution resolution)
    {
        var cacheKey = $"{videoId}_{resolution}";

        if (Cache.TryGetValue(cacheKey, out var cached))
            return new PreviewResult(cached, DateTime.UtcNow);

        var video = await _repository.GetByIdAsync(videoId);
        if (video is null) return null;

        var videoPath = Path.Combine(_libraryRootPath, video.RelativePath);
        if (!File.Exists(videoPath)) return null;

        var data = await Task.Run(() => GeneratePreview(videoPath, resolution));
        if (data is null) return null;

        Cache[cacheKey] = data;
        return new PreviewResult(data, DateTime.UtcNow);
    }

    private static (int Width, int Height) GetFrameSize(PreviewResolution resolution) => resolution switch
    {
        PreviewResolution.Preview360 => (640, 360),
        _ => (256, 144)
    };

    private byte[]? GeneratePreview(string videoPath, PreviewResolution resolution)
    {
        var (width, height) = GetFrameSize(resolution);

        try
        {
            var ffmpeg = new FFMpegConverter();

            using var ms = new MemoryStream();

            ffmpeg.ConvertMedia(videoPath, null, ms, Format.mp4, new ConvertSettings
            {
                Seek = 60f,
                MaxDuration = 25f,
                CustomOutputArgs = $"-an -preset ultrafast -vf scale={width}:{height}"
            });

            _logger.LogInformation("Generated {Resolution} preview for {VideoPath} ({Size} bytes)",
                resolution, videoPath, ms.Length);

            return ms.Length > 0 ? ms.ToArray() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate {Resolution} preview for {VideoPath}",
                resolution, videoPath);
            return null;
        }
    }
}
