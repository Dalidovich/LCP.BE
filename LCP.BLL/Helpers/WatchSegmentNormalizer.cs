using LCP.BLL.DTOs;
using LCP.Domain.Entities;

namespace LCP.BLL.Helpers;

public static class WatchSegmentNormalizer
{
    public const double MinDurationSeconds = 5;

    public static List<WatchSegment> Normalize(IEnumerable<WatchSegmentRequest> segments) =>
        [.. segments
            .Where(s => s.Duration >= MinDurationSeconds)
            .Select(s => new WatchSegment
            {
                Start = ToWholeSeconds(s.Start),
                Duration = ToWholeSeconds(s.Duration)
            })];

    private static int ToWholeSeconds(double seconds) =>
        (int)Math.Round(seconds, MidpointRounding.AwayFromZero);
}
