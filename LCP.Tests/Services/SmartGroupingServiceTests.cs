using LCP.BLL.Services;
using LCP.Domain.Entities;
using LCP.Tests.Fakes;

namespace LCP.Tests.Services;

public class SmartGroupingServiceTests
{
    private static VideoMetadata Video(string systemName, string? collectionId = null, string? id = null) =>
        new() { Id = id ?? systemName, SystemName = systemName, CollectionId = collectionId };

    private static async Task<Dictionary<string, string?>> GroupAsync(params VideoMetadata[] videos)
    {
        var repository = new InMemoryVideoRepository(videos);
        await new SmartGroupingService(repository).GroupVideosAsync();

        var snapshot = await repository.GetSnapshotAsync();
        return snapshot.ToDictionary(v => v.Id, v => v.CollectionId);
    }

    [Fact]
    public async Task GroupVideos_SharedCleanName_FormsACollection()
    {
        var result = await GroupAsync(
            Video("funny video cat 1"),
            Video("funny video cat ep 5"));

        Assert.Equal("funny video cat", result["funny video cat 1"]);
        Assert.Equal("funny video cat", result["funny video cat ep 5"]);
    }

    [Fact]
    public async Task GroupVideos_LoneVideo_GoesToDefault()
    {
        var result = await GroupAsync(Video("funny video bird"));

        Assert.Equal("default", result["funny video bird"]);
    }

    [Fact]
    public async Task GroupVideos_ExistingCollectionId_IsNeverTouched()
    {
        var result = await GroupAsync(
            Video("funny video cat 1", "manual"),
            Video("funny video cat 2"),
            Video("funny video cat 3"));

        Assert.Equal("manual", result["funny video cat 1"]);
        Assert.Equal("funny video cat", result["funny video cat 2"]);
        Assert.Equal("funny video cat", result["funny video cat 3"]);
    }

    [Fact]
    public async Task GroupVideos_NothingToChange_DoesNotPersist()
    {
        var repository = new InMemoryVideoRepository(
            Video("a", "manual"),
            Video("b", "manual"));

        await new SmartGroupingService(repository).GroupVideosAsync();

        Assert.Equal(0, repository.SaveCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GroupVideos_UnparsableSystemName_GoesToDefault(string systemName)
    {
        var result = await GroupAsync(Video(systemName, id: "x"));

        Assert.Equal("default", result["x"]);
    }

    [Fact]
    public async Task GroupVideos_SoloAbsorbedIntoMultiVideoGroupByPrefix()
    {
        var result = await GroupAsync(
            Video("funny video dog ep1"),
            Video("funny video dog ep2"),
            Video("funny video dog and puppet"));

        Assert.Equal("funny video dog", result["funny video dog ep1"]);
        Assert.Equal("funny video dog", result["funny video dog ep2"]);
        Assert.Equal("funny video dog", result["funny video dog and puppet"]);
    }

    [Fact]
    public async Task GroupVideos_SeveralMatchingPrefixes_LongestKeyWins()
    {
        var result = await GroupAsync(
            Video("the show 1"),
            Video("the show 2"),
            Video("the show extra 1"),
            Video("the show extra 2"),
            Video("the show extra bonus"));

        Assert.Equal("the show", result["the show 1"]);
        Assert.Equal("the show extra", result["the show extra 1"]);
        Assert.Equal("the show extra", result["the show extra bonus"]);
    }

    [Fact]
    public async Task GroupVideos_AbsorptionIsNotTransitive_SoloGroupsAreNeverTargets()
    {
        var result = await GroupAsync(
            Video("cat"),
            Video("cat dog"));

        Assert.Equal("default", result["cat"]);
        Assert.Equal("default", result["cat dog"]);
    }

    [Fact]
    public async Task GroupVideos_GroupFormedByAbsorption_DoesNotBecomeANewTarget()
    {
        var result = await GroupAsync(
            Video("alpha 1"),
            Video("alpha 2"),
            Video("alpha beta"),
            Video("gamma"),
            Video("gamma delta"));

        Assert.Equal("alpha", result["alpha beta"]);
        Assert.Equal("default", result["gamma"]);
        Assert.Equal("default", result["gamma delta"]);
    }

    [Fact]
    public async Task GroupVideos_OutcomeDoesNotDependOnInputOrder()
    {
        VideoMetadata[] videos =
        [
            Video("the show 1"),
            Video("the show 2"),
            Video("the show extra 1"),
            Video("the show extra 2"),
            Video("the show extra bonus"),
            Video("the show extra bonus reel"),
            Video("unrelated clip")
        ];

        var forward = await GroupAsync(videos);
        var reversed = await GroupAsync([.. videos.Reverse()]);

        Assert.Equal(Describe(forward), Describe(reversed));
    }

    [Theory]
    [InlineData("show ep")]
    [InlineData("show ep1")]
    [InlineData("show ep 12")]
    [InlineData("show EP 12")]
    [InlineData("show 3")]
    [InlineData("show   4")]
    public async Task GroupVideos_StripsEpisodeMarkersWhenBuildingTheKey(string systemName)
    {
        var result = await GroupAsync(Video(systemName), Video("show 99"));

        Assert.Equal("show", result[systemName]);
        Assert.Equal("show", result["show 99"]);
    }

    [Fact]
    public async Task GroupVideos_DoesNotStripEpInsideAWord()
    {
        var result = await GroupAsync(
            Video("episode 1"),
            Video("episode 2"));

        Assert.Equal("episode", result["episode 1"]);
        Assert.Equal("episode", result["episode 2"]);
    }

    [Fact]
    public async Task GroupVideos_DoesNotStripDigitsGluedToTheName()
    {
        var result = await GroupAsync(
            Video("show3", id: "first"),
            Video("show3", id: "second"));

        Assert.Equal("show3", result["first"]);
        Assert.Equal("show3", result["second"]);
    }

    [Fact]
    public async Task GroupVideos_NameThatCleansToNothing_FallsBackToTheOriginal()
    {
        var result = await GroupAsync(
            Video("ep 1", id: "first"),
            Video("ep 1", id: "second"));

        Assert.Equal("ep 1", result["first"]);
        Assert.Equal("ep 1", result["second"]);
    }

    [Fact]
    public async Task GroupVideos_KeyMatchingIsCaseInsensitive()
    {
        var result = await GroupAsync(
            Video("The Show 1"),
            Video("the show 2"));

        Assert.Equal(result["The Show 1"], result["the show 2"]);
        Assert.NotEqual("default", result["The Show 1"]);
    }

    private static string Describe(Dictionary<string, string?> assignments) =>
        string.Join("|", assignments.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).Select(kvp => $"{kvp.Key}={kvp.Value}"));
}
