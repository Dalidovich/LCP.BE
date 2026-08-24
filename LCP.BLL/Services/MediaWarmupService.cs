using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Interfaces;
using Microsoft.Extensions.Logging;

namespace LCP.BLL.Services;

public class MediaWarmupService : IMediaWarmupService
{
    private const int MaxParallelWarmups = 2;

    private readonly IThumbnailService _thumbnailService;
    private readonly IPreviewService _previewService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<MediaWarmupService> _logger;
    private readonly SemaphoreSlim _passGate = new(1, 1);

    public MediaWarmupService(
        IThumbnailService thumbnailService,
        IPreviewService previewService,
        ISettingsRepository settingsRepository,
        ILogger<MediaWarmupService> logger)
    {
        _thumbnailService = thumbnailService;
        _previewService = previewService;
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    public void QueueWarm(IReadOnlyList<string> videoIds, CancellationToken cancellationToken)
    {
        if (videoIds.Count == 0) return;

        if (!_passGate.Wait(0))
        {
            _logger.LogDebug("Skipping cache warm pass, another pass is already running");
            return;
        }

        var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = RunPassAsync(videoIds, linkedSource);
    }

    private async Task RunPassAsync(IReadOnlyList<string> videoIds, CancellationTokenSource linkedSource)
    {
        try
        {
            var settings = await _settingsRepository.GetAsync();
            if (settings is null || !settings.WarmCache) return;

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxParallelWarmups,
                CancellationToken = linkedSource.Token
            };

            await Parallel.ForEachAsync(videoIds, parallelOptions, async (videoId, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                await WarmOneAsync(videoId);
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Cache warm pass was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cache warm pass failed");
        }
        finally
        {
            linkedSource.Dispose();
            _passGate.Release();
        }
    }

    private async Task WarmOneAsync(string videoId)
    {
        try
        {
            await _thumbnailService.GetThumbnailAsync(videoId);
            await _previewService.GetPreviewAsync(videoId, PreviewResolution.Preview144);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to warm cache for video {VideoId}", videoId);
        }
    }
}
