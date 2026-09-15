using LCP.BLL.DTOs;

namespace LCP.BLL.Interfaces;

public interface IWatchRecordService
{
    Task RecordAsync(string videoId, string nameEn, IReadOnlyList<WatchSegmentRequest> segments);
}
