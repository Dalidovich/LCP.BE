using LCP.BLL.DTOs;
using LCP.Domain.Entities;

namespace LCP.BLL.Helpers;

public static class WatchSegmentNormalizer
{
    public static List<WatchSegment> Normalize(IEnumerable<WatchSegmentRequest> segments, double minDurationSeconds) =>
        [.. segments
            .Where(s => s.Duration >= minDurationSeconds)
            .Select(s => new WatchSegment
            {
                Start = ToWholeSeconds(s.Start),
                Duration = ToWholeSeconds(s.Duration)
            })];

    private static int ToWholeSeconds(double seconds) =>
        (int)Math.Round(seconds, MidpointRounding.AwayFromZero);
}
