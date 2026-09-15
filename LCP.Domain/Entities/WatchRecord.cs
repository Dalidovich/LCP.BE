namespace LCP.Domain.Entities;

public class WatchRecord
{
    public string VideoId { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public DateTime WatchedAt { get; set; }
    public List<WatchSegment> Segments { get; set; } = [];
}
