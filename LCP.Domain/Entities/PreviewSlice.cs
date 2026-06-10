namespace LCP.Domain.Entities;

public class PreviewSlice
{
    public double Start { get; set; }
    public double Duration { get; set; }

    public static List<PreviewSlice> CalculateSlices(double duration)
    {
        const int count = 5;
        const double sliceDuration = 5;
        const double totalDuration = count * sliceDuration;

        if (duration <= totalDuration)
            return [new PreviewSlice { Start = 0, Duration = duration }];

        var startMargin = Math.Min(10, duration * 0.05);
        var endMargin = 5.0;
        var usable = duration - startMargin - endMargin;
        var gap = (usable - totalDuration) / (count - 1);

        var slices = new List<PreviewSlice>(count);
        for (var i = 0; i < count; i++)
        {
            var start = startMargin + i * (sliceDuration + gap);
            slices.Add(new PreviewSlice { Start = Math.Round(start, 1), Duration = sliceDuration });
        }
        return slices;
    }
}
