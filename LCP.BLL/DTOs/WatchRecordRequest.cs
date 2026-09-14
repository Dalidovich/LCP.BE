namespace LCP.BLL.DTOs;

public record WatchRecordRequest(string VideoId, List<WatchSegmentRequest> Segments);
