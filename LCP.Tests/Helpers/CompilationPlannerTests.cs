using LCP.BLL.Helpers;
using LCP.DAL.Configuration;
using LCP.Domain.Entities;

namespace LCP.Tests.Helpers;

public class CompilationPlannerTests
{
    private static WatchRecord Record(string videoId, params (int Start, int Duration)[] segments) => new()
    {
        VideoId = videoId,
        Segments = [.. segments.Select(s => new WatchSegment { Start = s.Start, Duration = s.Duration })]
    };

    private static Dictionary<string, double> Durations(params (string Id, double Duration)[] videos) =>
        videos.ToDictionary(v => v.Id, v => v.Duration);

    private static CompilationSettings Settings(Action<CompilationSettings>? configure = null)
    {
        var settings = new CompilationSettings
        {
            MaxDurationSeconds = 600,
            MergeGapSeconds = 3,
            MinMomentSeconds = 5,
            MaxMomentSeconds = 30,
            MaxCandidatesPerCluster = 3,
            MinDistanceSeconds = 5,
            MaxCoveragePercent = 1,
            MaxCoverageSeconds = 10000,
            VideoRepeatPenalty = 0.5
        };
        configure?.Invoke(settings);
        return settings;
    }

    [Fact]
    public void BuildClusters_MergesOverlappingMoments()
    {
        var clusters = CompilationPlanner.BuildClusters(
            [Record("a", (600, 10)), Record("a", (605, 10)), Record("a", (608, 4))],
            Durations(("a", 3600)), 3);

        var cluster = Assert.Single(clusters);
        Assert.Equal(600, cluster.Start);
        Assert.Equal(615, cluster.End);
        Assert.Equal(3, cluster.MomentCount);
        Assert.Equal(24, cluster.WatchedSeconds);
    }

    [Fact]
    public void BuildClusters_MergesMomentsWithinTheGap()
    {
        var clusters = CompilationPlanner.BuildClusters(
            [Record("a", (600, 10), (612, 8))], Durations(("a", 3600)), 3);

        var cluster = Assert.Single(clusters);
        Assert.Equal(600, cluster.Start);
        Assert.Equal(620, cluster.End);
    }

    [Fact]
    public void BuildClusters_SplitsMomentsBeyondTheGap()
    {
        var clusters = CompilationPlanner.BuildClusters(
            [Record("a", (600, 10), (620, 8))], Durations(("a", 3600)), 3);

        Assert.Equal(2, clusters.Count);
    }

    [Fact]
    public void BuildClusters_SkipsVideosMissingFromTheLibrary()
    {
        var clusters = CompilationPlanner.BuildClusters(
            [Record("gone", (0, 10)), Record("a", (0, 10))], Durations(("a", 100)), 3);

        Assert.Equal("a", Assert.Single(clusters).VideoId);
    }

    [Fact]
    public void BuildClusters_ClipsMomentsToTheVideoDuration()
    {
        var clusters = CompilationPlanner.BuildClusters(
            [Record("a", (90, 30), (200, 10))], Durations(("a", 100)), 3);

        var cluster = Assert.Single(clusters);
        Assert.Equal(100, cluster.End);
    }

    [Fact]
    public void BuildClusters_DoesNotModifyTheRecords()
    {
        var record = Record("a", (600, 10), (605, 10));

        CompilationPlanner.BuildClusters([record], Durations(("a", 3600)), 3);

        Assert.Equal([600, 605], record.Segments.Select(s => s.Start));
        Assert.Equal([10, 10], record.Segments.Select(s => s.Duration));
    }

    [Fact]
    public void BuildCandidates_PicksTheHottestWindowInsideALongCluster()
    {
        var records = new List<WatchRecord> { Record("a", (0, 1000)) };
        for (var i = 0; i < 5; i++) records.Add(Record("a", (500, 20)));

        var clusters = CompilationPlanner.BuildClusters(records, Durations(("a", 1000)), 3);
        var candidates = CompilationPlanner.BuildCandidates(clusters, Settings());

        var best = candidates.OrderByDescending(c => c.Score).First();
        Assert.True(best.Start <= 500 && best.Start + best.Duration >= 520);
        Assert.Equal(3, candidates.Count);
    }

    [Fact]
    public void Select_RespectsTheTotalDurationLimit()
    {
        var records = Enumerable.Range(0, 20)
            .Select(i => Record($"v{i}", (100, 30)))
            .ToList();
        var durations = Durations([.. Enumerable.Range(0, 20).Select(i => ($"v{i}", 1000.0))]);

        var selected = CompilationPlanner.Plan(records, durations, Settings(s => s.MaxDurationSeconds = 100));

        Assert.True(selected.Sum(m => m.Duration) <= 100);
        Assert.Equal(100, selected.Sum(m => m.Duration));
    }

    [Fact]
    public void Select_RespectsTheCoverageLimitPerVideo()
    {
        var records = Enumerable.Range(0, 20)
            .Select(i => Record("a", (i * 100, 30), (i * 100, 30)))
            .ToList();

        var selected = CompilationPlanner.Plan(records, Durations(("a", 2000)),
            Settings(s =>
            {
                s.MaxCoveragePercent = 0.05;
                s.MaxCoverageSeconds = 1000;
            }));

        Assert.True(selected.Sum(m => m.Duration) <= 100);
    }

    [Fact]
    public void Select_NeverReturnsOverlappingMoments()
    {
        var records = new List<WatchRecord>();
        for (var i = 0; i < 10; i++) records.Add(Record("a", (100 + i * 3, 40)));

        var selected = CompilationPlanner.Plan(records, Durations(("a", 1000)), Settings());

        var ordered = selected.OrderBy(m => m.Start).ToList();
        for (var i = 1; i < ordered.Count; i++)
            Assert.True(ordered[i].Start >= ordered[i - 1].Start + ordered[i - 1].Duration);
    }

    [Fact]
    public void Select_PrefersDiversityWhenPopularityIsComparable()
    {
        var records = new List<WatchRecord>
        {
            Record("a", (100, 20), (100, 20), (100, 20)),
            Record("a", (300, 20), (300, 20), (300, 20)),
            Record("b", (100, 20), (100, 20), (100, 20))
        };

        var selected = CompilationPlanner.Plan(records, Durations(("a", 1000), ("b", 1000)), Settings());

        Assert.Equal(["a", "b", "a"], selected.Select(m => m.VideoId));
    }

    [Fact]
    public void Plan_IsDeterministic()
    {
        var records = new List<WatchRecord>
        {
            Record("b", (10, 20)),
            Record("a", (10, 20)),
            Record("c", (50, 15), (52, 15))
        };
        var durations = Durations(("a", 100), ("b", 100), ("c", 100));

        var first = CompilationPlanner.Plan(records, durations, Settings());
        var second = CompilationPlanner.Plan(records, durations, Settings());

        Assert.Equal(first, second);
    }

    [Fact]
    public void Plan_EmptyStatistics_ReturnsNothing()
    {
        Assert.Empty(CompilationPlanner.Plan([], Durations(("a", 100)), Settings()));
    }
}
