namespace LCP.Domain.Entities;

public class WatchRecord
{
    public string VideoId { get; set; } = string.Empty;
    public List<WatchSegment> Segments { get; set; } = [];
}
