using LCP.BLL.DTOs;
using LCP.BLL.Helpers;
using LCP.BLL.Interfaces;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;

namespace LCP.BLL.Services;

public class WatchRecordService : IWatchRecordService
{
    private readonly IWatchRecordRepository _repository;

    public WatchRecordService(IWatchRecordRepository repository)
    {
        _repository = repository;
    }

    public async Task RecordAsync(string videoId, IReadOnlyList<WatchSegmentRequest> segments)
    {
        var normalized = WatchSegmentNormalizer.Normalize(segments);
        if (normalized.Count == 0) return;

        await _repository.AppendAsync(new WatchRecord { VideoId = videoId, Segments = normalized });
    }
}
