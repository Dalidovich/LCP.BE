using LCP.BLL.DTOs;
using LCP.BLL.Helpers;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.Extensions.Options;

namespace LCP.BLL.Services;

public class WatchRecordService : IWatchRecordService
{
    private readonly IWatchRecordRepository _repository;
    private readonly double _minSegmentSeconds;

    public WatchRecordService(IWatchRecordRepository repository, IOptions<LibrarySettings> settings)
    {
        _repository = repository;
        _minSegmentSeconds = settings.Value.MinWatchSegmentSeconds;
    }

    public async Task RecordAsync(string videoId, string nameEn, IReadOnlyList<WatchSegmentRequest> segments)
    {
        var normalized = WatchSegmentNormalizer.Normalize(segments, _minSegmentSeconds);
        if (normalized.Count == 0) return;

        await _repository.AppendAsync(new WatchRecord
        {
            VideoId = videoId,
            NameEn = nameEn,
            WatchedAt = DateTime.UtcNow,
            Segments = normalized
        });
    }
}
