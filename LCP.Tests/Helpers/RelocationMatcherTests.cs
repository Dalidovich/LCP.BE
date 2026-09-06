using LCP.BLL.Helpers;
using LCP.Domain.Entities;

namespace LCP.Tests.Helpers;

public class RelocationMatcherTests
{
    private static VideoMetadata Entry(string relativePath, double duration = 0) =>
        new() { Id = relativePath, RelativePath = relativePath, Duration = duration };

    private static Dictionary<string, double> Probed(params (string Path, double Duration)[] probed) =>
        probed.ToDictionary(p => p.Path, p => p.Duration, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Match_NoMissingEntries_ReturnsNothing()
    {
        var result = RelocationMatcher.Match([], ["new\\clip.mp4"], Probed(("new\\clip.mp4", 100)));

        Assert.Empty(result);
    }

    [Fact]
    public void Match_NoUntrackedPaths_ReturnsNothing()
    {
        var result = RelocationMatcher.Match([Entry("old\\clip.mp4", 100)], [], Probed());

        Assert.Empty(result);
    }

    [Fact]
    public void Match_SameFileNameMovedToAnotherFolder_IsMatched()
    {
        var entry = Entry("old\\clip.mp4", 100);

        var result = RelocationMatcher.Match([entry], ["new\\clip.mp4"], Probed(("new\\clip.mp4", 100)));

        var relocation = Assert.Single(result);
        Assert.Same(entry, relocation.Entry);
        Assert.Equal("new\\clip.mp4", relocation.NewPath);
    }

    [Fact]
    public void Match_FileNameComparisonIsCaseInsensitive()
    {
        var entry = Entry("old\\Clip.MP4", 100);

        var result = RelocationMatcher.Match([entry], ["new\\clip.mp4"], Probed(("new\\clip.mp4", 100)));

        Assert.Single(result);
    }

    [Fact]
    public void Match_RenamedFile_IsNeverMatched()
    {
        var entry = Entry("old\\clip.mp4", 100);

        var result = RelocationMatcher.Match([entry], ["new\\renamed.mp4"], Probed(("new\\renamed.mp4", 100)));

        Assert.Empty(result);
    }

    [Fact]
    public void Match_DurationWithinTolerance_IsMatched()
    {
        var entry = Entry("old\\clip.mp4", 100);

        var result = RelocationMatcher.Match([entry], ["new\\clip.mp4"], Probed(("new\\clip.mp4", 100.9)));

        Assert.Single(result);
    }

    [Fact]
    public void Match_UnknownEntryDuration_StillPairsTheLoneLeftover()
    {
        var entry = Entry("old\\clip.mp4");

        var result = RelocationMatcher.Match([entry], ["new\\clip.mp4"], Probed(("new\\clip.mp4", 100)));

        Assert.Single(result);
    }

    [Fact]
    public void Match_UnknownProbedDuration_StillPairsTheLoneLeftover()
    {
        var entry = Entry("old\\clip.mp4", 100);

        var result = RelocationMatcher.Match([entry], ["new\\clip.mp4"], Probed(("new\\clip.mp4", 0)));

        Assert.Single(result);
    }

    [Fact]
    public void Match_LoneLeftoverWithConflictingDurations_IsNotPaired()
    {
        var entry = Entry("old\\clip.mp4", 100);

        var result = RelocationMatcher.Match([entry], ["new\\clip.mp4"], Probed(("new\\clip.mp4", 500)));

        Assert.Empty(result);
    }

    [Fact]
    public void Match_TwoSameNamedFiles_AreResolvedByDuration()
    {
        var first = Entry("a\\clip.mp4", 100);
        var second = Entry("b\\clip.mp4", 500);

        var result = RelocationMatcher.Match(
            [first, second],
            ["x\\clip.mp4", "y\\clip.mp4"],
            Probed(("x\\clip.mp4", 500), ("y\\clip.mp4", 100)));

        Assert.Equal(2, result.Count);
        Assert.Equal("y\\clip.mp4", result.Single(r => ReferenceEquals(r.Entry, first)).NewPath);
        Assert.Equal("x\\clip.mp4", result.Single(r => ReferenceEquals(r.Entry, second)).NewPath);
    }

    [Fact]
    public void Match_AmbiguousSameNamedFilesWithEqualDurations_AreLeftAlone()
    {
        var result = RelocationMatcher.Match(
            [Entry("a\\clip.mp4", 100), Entry("b\\clip.mp4", 100)],
            ["x\\clip.mp4", "y\\clip.mp4"],
            Probed(("x\\clip.mp4", 100), ("y\\clip.mp4", 100)));

        Assert.Empty(result);
    }

    [Fact]
    public void Match_OneResolvedByDuration_LeavesTheRemainingPairToTheLeftoverRule()
    {
        var byDuration = Entry("a\\clip.mp4", 500);
        var leftover = Entry("b\\clip.mp4");

        var result = RelocationMatcher.Match(
            [byDuration, leftover],
            ["x\\clip.mp4", "y\\clip.mp4"],
            Probed(("x\\clip.mp4", 500), ("y\\clip.mp4", 0)));

        Assert.Equal(2, result.Count);
        Assert.Equal("x\\clip.mp4", result.Single(r => ReferenceEquals(r.Entry, byDuration)).NewPath);
        Assert.Equal("y\\clip.mp4", result.Single(r => ReferenceEquals(r.Entry, leftover)).NewPath);
    }

    [Fact]
    public void Match_ThreeCandidatesWithOneResolvable_LeavesTheAmbiguousRestAlone()
    {
        var resolvable = Entry("a\\clip.mp4", 900);

        var result = RelocationMatcher.Match(
            [resolvable, Entry("b\\clip.mp4", 100), Entry("c\\clip.mp4", 100)],
            ["x\\clip.mp4", "y\\clip.mp4", "z\\clip.mp4"],
            Probed(("x\\clip.mp4", 900), ("y\\clip.mp4", 100), ("z\\clip.mp4", 100)));

        var relocation = Assert.Single(result);
        Assert.Same(resolvable, relocation.Entry);
    }

    [Fact]
    public void Match_OneEntryWithSeveralEqualDurationCandidates_IsLeftAlone()
    {
        var result = RelocationMatcher.Match(
            [Entry("a/clip.mp4", 100)],
            ["x/clip.mp4", "y/clip.mp4"],
            Probed(("x/clip.mp4", 100), ("y/clip.mp4", 100)));

        Assert.Empty(result);
    }

    [Fact]
    public void Match_SeveralEntriesShareADurationWithOneCandidate_AreLeftAlone()
    {
        var result = RelocationMatcher.Match(
            [Entry("a/clip.mp4", 100), Entry("b/clip.mp4", 100)],
            ["x/clip.mp4"],
            Probed(("x/clip.mp4", 100)));

        Assert.Empty(result);
    }

    [Fact]
    public void Match_DifferentFileNames_AreMatchedIndependently()
    {
        var first = Entry("old\\one.mp4", 100);
        var second = Entry("old\\two.mp4", 200);

        var result = RelocationMatcher.Match(
            [first, second],
            ["new\\one.mp4", "new\\two.mp4"],
            Probed(("new\\one.mp4", 100), ("new\\two.mp4", 200)));

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Match_ForwardAndBackwardSlashes_AreTreatedAsTheSamePath()
    {
        var entry = Entry("old/clip.mp4", 100);

        var result = RelocationMatcher.Match([entry], ["new/clip.mp4"], Probed(("new/clip.mp4", 100)));

        Assert.Single(result);
    }

    [Fact]
    public void Match_MissingEntryWithNoCandidateFile_IsLeftAlone()
    {
        var result = RelocationMatcher.Match(
            [Entry("old\\clip.mp4", 100), Entry("old\\gone.mp4", 200)],
            ["new\\clip.mp4"],
            Probed(("new\\clip.mp4", 100)));

        var relocation = Assert.Single(result);
        Assert.Equal("old\\clip.mp4", relocation.Entry.RelativePath);
    }
}
