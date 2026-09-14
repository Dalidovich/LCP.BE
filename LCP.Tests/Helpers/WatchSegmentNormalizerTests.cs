using LCP.BLL.DTOs;
using LCP.BLL.Helpers;

namespace LCP.Tests.Helpers;

public class WatchSegmentNormalizerTests
{
    private const double MinSeconds = 5;

    [Fact]
    public void Normalize_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(WatchSegmentNormalizer.Normalize([], MinSeconds));
    }

    [Fact]
    public void Normalize_DropsSegmentsShorterThanTheMinimum()
    {
        var result = WatchSegmentNormalizer.Normalize(
        [
            new WatchSegmentRequest(0, 1),
            new WatchSegmentRequest(10, 4.99),
            new WatchSegmentRequest(20, 5)
        ], MinSeconds);

        var segment = Assert.Single(result);
        Assert.Equal(20, segment.Start);
        Assert.Equal(5, segment.Duration);
    }

    [Fact]
    public void Normalize_AppliesTheMinimumBeforeRounding()
    {
        Assert.Empty(WatchSegmentNormalizer.Normalize([new WatchSegmentRequest(30, 4.6)], MinSeconds));
    }

    [Fact]
    public void Normalize_RoundsStartAndDurationToWholeSeconds()
    {
        var segment = Assert.Single(WatchSegmentNormalizer.Normalize([new WatchSegmentRequest(21.4, 10.6)], MinSeconds));

        Assert.Equal(21, segment.Start);
        Assert.Equal(11, segment.Duration);
    }

    [Fact]
    public void Normalize_RoundsMidpointsAwayFromZero()
    {
        var segment = Assert.Single(WatchSegmentNormalizer.Normalize([new WatchSegmentRequest(20.5, 10.5)], MinSeconds));

        Assert.Equal(21, segment.Start);
        Assert.Equal(11, segment.Duration);
    }

    [Fact]
    public void Normalize_KeepsWatchOrderAndOverlaps()
    {
        var result = WatchSegmentNormalizer.Normalize(
        [
            new WatchSegmentRequest(50, 17),
            new WatchSegmentRequest(55, 15),
            new WatchSegmentRequest(10, 6)
        ], MinSeconds);

        Assert.Equal([50, 55, 10], result.Select(s => s.Start));
        Assert.Equal([17, 15, 6], result.Select(s => s.Duration));
    }

    [Fact]
    public void Normalize_ClickSeekExample_KeepsOnlyTheWatchedStretches()
    {
        var result = WatchSegmentNormalizer.Normalize(
        [
            new WatchSegmentRequest(0, 1.1),
            new WatchSegmentRequest(21.2, 10.1),
            new WatchSegmentRequest(50.3, 16.9)
        ], MinSeconds);

        Assert.Equal([21, 50], result.Select(s => s.Start));
        Assert.Equal([10, 17], result.Select(s => s.Duration));
    }
}
