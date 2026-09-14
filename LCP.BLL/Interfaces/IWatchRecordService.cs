using LCP.BLL.DTOs;

namespace LCP.BLL.Interfaces;

public interface IWatchRecordService
{
    Task RecordAsync(string videoId, IReadOnlyList<WatchSegmentRequest> segments);
}
