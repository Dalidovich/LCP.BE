namespace LCP.Domain.Entities;

public class PreviewSlice
{
    private const int SliceCount = 5;
    private const double SliceLength = 5;
    private const double TotalSliceLength = SliceCount * SliceLength;
    private const double PreferredStartMargin = 10;
    private const double PreferredEndMargin = 5;
    private const double BoundsTolerance = 0.1;

    public double Start { get; set; }
    public double Duration { get; set; }

    public PreviewSlice Clone() => new() { Start = Start, Duration = Duration };

    public static List<PreviewSlice> CalculateSlices(double duration)
    {
        if (duration <= TotalSliceLength)
            return [new PreviewSlice { Start = 0, Duration = duration }];

        var (offset, usable) = CalculateUsableRange(duration, Math.Min(PreferredStartMargin, duration * 0.05));
        var gap = Math.Max(0, (usable - TotalSliceLength) / (SliceCount - 1));

        var slices = new List<PreviewSlice>(SliceCount);
        for (var i = 0; i < SliceCount; i++)
        {
            slices.Add(CreateBounded(offset + i * (SliceLength + gap), duration));
        }
        return slices;
    }

    public static List<PreviewSlice> CalculateRandomSlices(double duration)
    {
        if (duration <= TotalSliceLength)
            return [new PreviewSlice { Start = 0, Duration = duration }];

        var rng = Random.Shared;
        var (offset, usable) = CalculateUsableRange(duration, rng.Next(5, 16));
        var zoneLength = usable / SliceCount;
        var jitterRange = Math.Max(0, zoneLength - SliceLength);

        var slices = new List<PreviewSlice>(SliceCount);
        for (var i = 0; i < SliceCount; i++)
        {
            var zoneStart = offset + i * zoneLength;
            slices.Add(CreateBounded(zoneStart + rng.NextDouble() * jitterRange, duration));
        }
        return slices;
    }

    public static bool AreWithinBounds(IReadOnlyList<PreviewSlice> slices, double duration)
    {
        var previousEnd = 0.0;
        foreach (var slice in slices)
        {
            if (slice.Start < -BoundsTolerance) return false;
            if (slice.Duration < 0) return false;
            if (slice.Start + slice.Duration > duration + BoundsTolerance) return false;
            if (slice.Start < previousEnd - BoundsTolerance) return false;
            previousEnd = slice.Start + slice.Duration;
        }
        return true;
    }

    private static (double Offset, double Usable) CalculateUsableRange(double duration, double startMargin)
    {
        var endMargin = PreferredEndMargin;
        var affordableMargin = duration - TotalSliceLength;
        var requestedMargin = startMargin + endMargin;

        if (requestedMargin > affordableMargin)
        {
            var scale = requestedMargin > 0 ? affordableMargin / requestedMargin : 0;
            startMargin *= scale;
            endMargin *= scale;
        }

        return (startMargin, duration - startMargin - endMargin);
    }

    private static PreviewSlice CreateBounded(double start, double duration)
    {
        var boundedStart = Math.Round(Math.Clamp(start, 0, duration), 1);
        var available = Math.Max(0, duration - boundedStart);
        return new PreviewSlice
        {
            Start = boundedStart,
            Duration = Math.Min(available, Math.Round(Math.Min(SliceLength, available), 1))
        };
    }
}
