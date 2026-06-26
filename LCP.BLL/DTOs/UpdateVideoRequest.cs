using LCP.Domain.Entities;

namespace LCP.BLL.DTOs;

public class UpdateVideoRequest
{
    public string? NameEn { get; set; }
    public string? NameLocal { get; set; }
    public string? CollectionId { get; set; }
    public int? EpisodeNumber { get; set; }
    public VideoType? Type { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? ProductionInfo { get; set; }
    public double? ThumbnailTimecode { get; set; }
    public DateTime? LastTimeWatched { get; set; }
}
