namespace LCP.Domain.Entities;

public class VideoMetadata
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string RelativePath { get; set; } = string.Empty;
    public string SystemName { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string NameLocal { get; set; } = string.Empty;
    public string? CollectionId { get; set; }
    public int EpisodeNumber { get; set; } = -1;
    public VideoType Type { get; set; } = VideoType.Film;
    public List<string> Tags { get; set; } = [];
    public List<string> ProductionInfo { get; set; } = [];
    public double ThumbnailTimecode { get; set; } = -1;
    public double Duration { get; set; }
    public DateTime? LastTimeWatched { get; set; }
    public List<PreviewSlice> PreviewSlices { get; set; } = [];

    public VideoMetadata Clone() => new()
    {
        Id = Id,
        RelativePath = RelativePath,
        SystemName = SystemName,
        NameEn = NameEn,
        NameLocal = NameLocal,
        CollectionId = CollectionId,
        EpisodeNumber = EpisodeNumber,
        Type = Type,
        Tags = [.. Tags],
        ProductionInfo = [.. ProductionInfo],
        ThumbnailTimecode = ThumbnailTimecode,
        Duration = Duration,
        LastTimeWatched = LastTimeWatched,
        PreviewSlices = [.. PreviewSlices.Select(s => s.Clone())]
    };
}
