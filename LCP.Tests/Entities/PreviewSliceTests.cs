using LCP.Domain.Entities;

namespace LCP.Tests.Entities;

public class PreviewSliceTests
{
    private const double TotalSliceLength = 25;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(12.5)]
    [InlineData(24.9)]
    [InlineData(25)]
    public void CalculateSlices_DurationAtOrBelowTotalLength_ReturnsSingleFullSpanSlice(double duration)
    {
        var slices = PreviewSlice.CalculateSlices(duration);

        var slice = Assert.Single(slices);
        Assert.Equal(0, slice.Start);
        Assert.Equal(duration, slice.Duration);
    }

    [Theory]
    [InlineData(25.1)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(600)]
    [InlineData(7200)]
    public void CalculateSlices_LongerDuration_ReturnsFiveSlices(double duration)
    {
        Assert.Equal(5, PreviewSlice.CalculateSlices(duration).Count);
    }

    [Theory]
    [InlineData(25.1)]
    [InlineData(26)]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(45)]
    [InlineData(120)]
    [InlineData(3600)]
    [InlineData(20000)]
    public void CalculateSlices_AlwaysProducesInBoundsNonOverlappingSlices(double duration)
    {
        var slices = PreviewSlice.CalculateSlices(duration);

        Assert.True(PreviewSlice.AreWithinBounds(slices, duration));
    }

    [Theory]
    [InlineData(25.1)]
    [InlineData(26)]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(120)]
    [InlineData(3600)]
    public void CalculateRandomSlices_AlwaysProducesInBoundsNonOverlappingSlices(double duration)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var slices = PreviewSlice.CalculateRandomSlices(duration);

            Assert.True(
                PreviewSlice.AreWithinBounds(slices, duration),
                $"attempt {attempt} produced out-of-bounds slices for duration {duration}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(25)]
    public void CalculateRandomSlices_ShortDuration_ReturnsSingleFullSpanSlice(double duration)
    {
        var slice = Assert.Single(PreviewSlice.CalculateRandomSlices(duration));

        Assert.Equal(0, slice.Start);
        Assert.Equal(duration, slice.Duration);
    }

    [Fact]
    public void CalculateSlices_LongDuration_HonoursPreferredMargins()
    {
        var slices = PreviewSlice.CalculateSlices(600);

        Assert.Equal(10, slices[0].Start);
        Assert.True(slices[^1].Start + slices[^1].Duration <= 595.1);
    }

    [Fact]
    public void CalculateSlices_LongDuration_SpreadsSlicesEvenly()
    {
        var slices = PreviewSlice.CalculateSlices(600);
        var gaps = slices
            .Zip(slices.Skip(1), (a, b) => b.Start - a.Start)
            .ToList();

        Assert.All(gaps, gap => Assert.Equal(gaps[0], gap, 1));
    }

    [Fact]
    public void CalculateSlices_LongDuration_SpansTheWholeUsableRange()
    {
        var last = PreviewSlice.CalculateSlices(600)[^1];

        Assert.Equal(595, last.Start + last.Duration, 1);
    }

    [Fact]
    public void CalculateSlices_LongDuration_UsesFullSliceLength()
    {
        Assert.All(PreviewSlice.CalculateSlices(600), s => Assert.Equal(5, s.Duration));
    }

    [Fact]
    public void CalculateSlices_TightDuration_ShrinksMarginsProportionally()
    {
        var slices = PreviewSlice.CalculateSlices(26);

        Assert.True(slices[0].Start > 0);
        Assert.True(slices[0].Start < 10);
        Assert.True(PreviewSlice.AreWithinBounds(slices, 26));
    }

    [Fact]
    public void CalculateSlices_TightDuration_SlicesAbutButNeverOverlap()
    {
        var slices = PreviewSlice.CalculateSlices(25.5);

        for (var i = 1; i < slices.Count; i++)
        {
            var previousEnd = slices[i - 1].Start + slices[i - 1].Duration;
            Assert.True(slices[i].Start >= previousEnd - 0.1);
        }
    }

    [Fact]
    public void CalculateSlices_NeverRunsPastTheEnd()
    {
        foreach (var duration in new[] { 25.1, 25.5, 26, 27, 29, 31, 40, 55 })
        {
            var last = PreviewSlice.CalculateSlices(duration)[^1];
            Assert.True(
                last.Start + last.Duration <= duration + 0.1,
                $"last slice overran duration {duration}");
        }
    }

    [Fact]
    public void AreWithinBounds_EmptyList_IsValid()
    {
        Assert.True(PreviewSlice.AreWithinBounds([], 100));
    }

    [Fact]
    public void AreWithinBounds_NegativeStart_IsRejected()
    {
        Assert.False(PreviewSlice.AreWithinBounds([new PreviewSlice { Start = -1, Duration = 5 }], 100));
    }

    [Fact]
    public void AreWithinBounds_NegativeDuration_IsRejected()
    {
        Assert.False(PreviewSlice.AreWithinBounds([new PreviewSlice { Start = 0, Duration = -5 }], 100));
    }

    [Fact]
    public void AreWithinBounds_SliceBeyondDuration_IsRejected()
    {
        Assert.False(PreviewSlice.AreWithinBounds([new PreviewSlice { Start = 98, Duration = 5 }], 100));
    }

    [Fact]
    public void AreWithinBounds_OverlappingSlices_AreRejected()
    {
        List<PreviewSlice> slices =
        [
            new() { Start = 0, Duration = 5 },
            new() { Start = 3, Duration = 5 }
        ];

        Assert.False(PreviewSlice.AreWithinBounds(slices, 100));
    }

    [Fact]
    public void AreWithinBounds_UnorderedSlices_AreRejected()
    {
        List<PreviewSlice> slices =
        [
            new() { Start = 50, Duration = 5 },
            new() { Start = 10, Duration = 5 }
        ];

        Assert.False(PreviewSlice.AreWithinBounds(slices, 100));
    }

    [Fact]
    public void AreWithinBounds_AbuttingSlices_AreAccepted()
    {
        List<PreviewSlice> slices =
        [
            new() { Start = 0, Duration = 5 },
            new() { Start = 5, Duration = 5 }
        ];

        Assert.True(PreviewSlice.AreWithinBounds(slices, 100));
    }

    [Fact]
    public void AreWithinBounds_SlicesFromLegacyLongerVideo_AreRejected()
    {
        var slices = PreviewSlice.CalculateSlices(600);

        Assert.False(PreviewSlice.AreWithinBounds(slices, 60));
    }

    [Fact]
    public void Clone_CopiesValuesIntoADetachedInstance()
    {
        var original = new PreviewSlice { Start = 12, Duration = 5 };
        var clone = original.Clone();
        clone.Start = 99;

        Assert.Equal(12, original.Start);
        Assert.Equal(5, clone.Duration);
    }
}
