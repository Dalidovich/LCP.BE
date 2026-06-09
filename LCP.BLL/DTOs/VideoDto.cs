using LCP.Domain.Entities;

namespace LCP.BLL.DTOs;

public class VideoDto
{
    public string Id { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string SystemName { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string NameLocal { get; set; } = string.Empty;
    public string? CollectionId { get; set; }
    public int EpisodeNumber { get; set; } = -1;
    public VideoType Type { get; set; } = VideoType.Film;
    public List<string> Tags { get; set; } = [];
    public bool IsDeleted { get; set; }
}
