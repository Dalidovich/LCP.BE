using LCP.BLL.Helpers;
using LCP.Domain.Entities;

namespace LCP.Tests.Helpers;

public class SearchHelperTests
{
    [Fact]
    public void TrigramSimilarity_IdenticalText_ReturnsOne()
    {
        Assert.Equal(1.0, SearchHelper.TrigramSimilarity("inception", "inception"), 6);
    }

    [Fact]
    public void TrigramSimilarity_IgnoresCaseAndSurroundingWhitespace()
    {
        Assert.Equal(1.0, SearchHelper.TrigramSimilarity("  Inception  ", "inception"), 6);
    }

    [Fact]
    public void TrigramSimilarity_CollapsesInnerWhitespace()
    {
        Assert.Equal(
            SearchHelper.TrigramSimilarity("the matrix", "matrix"),
            SearchHelper.TrigramSimilarity("the\t \nmatrix", "matrix"),
            6);
    }

    [Fact]
    public void TrigramSimilarity_BothEmpty_ReturnsOne()
    {
        Assert.Equal(1.0, SearchHelper.TrigramSimilarity("", "   "), 6);
    }

    [Fact]
    public void TrigramSimilarity_OneSideEmpty_ReturnsZero()
    {
        Assert.Equal(0.0, SearchHelper.TrigramSimilarity("inception", ""), 6);
        Assert.Equal(0.0, SearchHelper.TrigramSimilarity("", "inception"), 6);
    }

    [Fact]
    public void TrigramSimilarity_NeverExceedsOne()
    {
        Assert.InRange(SearchHelper.TrigramSimilarity("cat", "cat"), 0.0, 1.0);
        Assert.InRange(SearchHelper.TrigramSimilarity("cat cat cat", "cat"), 0.0, 1.0);
    }

    [Fact]
    public void TrigramSimilarity_UnrelatedText_ScoresBelowCutoff()
    {
        Assert.True(SearchHelper.TrigramSimilarity("inception", "zulu warriors") < SearchHelper.MinScore);
    }

    [Fact]
    public void TrigramSimilarity_TypoInQuery_StillScoresAboveCutoff()
    {
        Assert.True(SearchHelper.TrigramSimilarity("inception", "inceptoin") >= SearchHelper.MinScore);
    }

    [Fact]
    public void TrigramSimilarity_QueryLongerThanText_GetsNoAffinityBoost()
    {
        var score = SearchHelper.TrigramSimilarity("cat", "cat and dog and bird");
        Assert.True(score < SearchHelper.MinScore);
    }

    [Fact]
    public void TrigramSimilarity_PrefixOfFirstWord_BeatsContainment()
    {
        var prefix = SearchHelper.TrigramSimilarity("matrix reloaded", "matrix");
        var contained = SearchHelper.TrigramSimilarity("thematrixreloaded", "matrix");

        Assert.True(prefix > contained);
    }

    [Fact]
    public void TrigramSimilarity_PrefixOfLaterWord_CountsAsWordPrefix()
    {
        var laterWord = SearchHelper.TrigramSimilarity("the great escape", "escape");
        var midWord = SearchHelper.TrigramSimilarity("thegreatescape", "escape");

        Assert.True(laterWord > midWord);
    }

    [Fact]
    public void TrigramSimilarity_ContainedQuery_ScoresAboveUnrelated()
    {
        var contained = SearchHelper.TrigramSimilarity("xxxinceptionxxx", "inception");
        var unrelated = SearchHelper.TrigramSimilarity("xxxsomethingxxx", "inception");

        Assert.True(contained > unrelated);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab")]
    public void TrigramSimilarity_ShortQuery_CannotClaimFullConfidence(string query)
    {
        var affinityOnly = SearchHelper.TrigramSimilarity(query + "xxxxxxxxxxxxxxxxxxxx", query);

        Assert.True(affinityOnly < 0.8);
    }

    [Fact]
    public void TrigramSimilarity_ThreeCharQuery_ReachesFullConfidence()
    {
        var two = SearchHelper.TrigramSimilarity("abxxxxxxxxxxxxxxxxxxxx", "ab");
        var three = SearchHelper.TrigramSimilarity("abcxxxxxxxxxxxxxxxxxxx", "abc");

        Assert.True(three > two);
    }

    [Fact]
    public void TrigramSimilarity_IsNotSymmetric()
    {
        var forward = SearchHelper.TrigramSimilarity("matrix reloaded", "matrix");
        var backward = SearchHelper.TrigramSimilarity("matrix", "matrix reloaded");

        Assert.True(forward > backward);
    }

    [Fact]
    public void ScoreVideo_TakesBestFieldScore()
    {
        var video = new VideoMetadata
        {
            SystemName = "zzz_unrelated_file",
            NameEn = "Inception",
            NameLocal = "qqq"
        };

        Assert.Equal(SearchHelper.TrigramSimilarity("Inception", "inception"), SearchHelper.ScoreVideo(video, "inception"), 6);
    }

    [Fact]
    public void ScoreVideo_SkipsEmptyFields()
    {
        var video = new VideoMetadata
        {
            SystemName = "inception",
            NameEn = string.Empty,
            NameLocal = "   "
        };

        Assert.True(SearchHelper.ScoreVideo(video, "inception") >= SearchHelper.MinScore);
    }

    [Fact]
    public void ScoreVideo_MatchesLocalName()
    {
        var video = new VideoMetadata
        {
            SystemName = "vid_0001",
            NameLocal = "Nachalo"
        };

        Assert.True(SearchHelper.ScoreVideo(video, "nachalo") >= SearchHelper.MinScore);
    }

    [Fact]
    public void IsMatch_AgreesWithMinScore()
    {
        var video = new VideoMetadata { SystemName = "The Matrix" };

        Assert.True(SearchHelper.IsMatch(video, "matrix"));
        Assert.False(SearchHelper.IsMatch(video, "unrelated title"));
    }

    [Fact]
    public void ScoreVideo_RanksExactTitleAboveWeakerCandidates()
    {
        var exact = new VideoMetadata { NameEn = "The Matrix" };
        var prefixed = new VideoMetadata { NameEn = "The Matrix Reloaded" };
        var unrelated = new VideoMetadata { NameEn = "Inception" };

        var exactScore = SearchHelper.ScoreVideo(exact, "the matrix");
        var prefixedScore = SearchHelper.ScoreVideo(prefixed, "the matrix");
        var unrelatedScore = SearchHelper.ScoreVideo(unrelated, "the matrix");

        Assert.True(exactScore > prefixedScore);
        Assert.True(prefixedScore > unrelatedScore);
    }

    [Fact]
    public void TrigramSimilarity_WordPrefixOfALongTitle_ClearsTheCutoffOnAffinityAlone()
    {
        const string title = "the lord of the rings the fellowship of the ring extended edition";

        var jaccardOnly = JaccardOf(title, "lord");
        var score = SearchHelper.TrigramSimilarity(title, "lord");

        Assert.True(jaccardOnly < SearchHelper.MinScore);
        Assert.True(score >= SearchHelper.MinScore);
    }

    [Fact]
    public void TrigramSimilarity_WordPrefix_ScoresAtLeastThePrefixWeight()
    {
        const string title = "the lord of the rings the fellowship of the ring extended edition";

        Assert.True(SearchHelper.TrigramSimilarity(title, "lord") >= 0.8);
    }

    [Fact]
    public void TrigramSimilarity_MidWordContainment_ScoresBetweenContainmentAndPrefixWeights()
    {
        const string title = "aaaaaaaaaaaaaaaaaaaaaaaalordaaaaaaaaaaaaaaaaaaaaaaaa";

        var score = SearchHelper.TrigramSimilarity(title, "lord");

        Assert.True(score >= 0.6);
        Assert.True(score < 0.8);
    }

    [Fact]
    public void TrigramSimilarity_TwoCharQuery_ScalesTheAffinityToTwoThirds()
    {
        const string title = "lo aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        var score = SearchHelper.TrigramSimilarity(title, "lo");

        Assert.True(score >= 0.8 * 2 / 3.0);
        Assert.True(score < 0.8);
    }

    [Fact]
    public void TrigramSimilarity_SingleCharQuery_ScalesTheAffinityToOneThird()
    {
        const string title = "l aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        var score = SearchHelper.TrigramSimilarity(title, "l");

        Assert.True(score >= 0.8 / 3.0);
        Assert.True(score < 0.8 * 2 / 3.0);
    }

    [Fact]
    public void TrigramSimilarity_NoAffinity_FallsBackToPlainJaccard()
    {
        const string title = "the matrix reloaded";

        Assert.Equal(JaccardOf(title, "reloadedx matrixx"), SearchHelper.TrigramSimilarity(title, "reloadedx matrixx"), 6);
    }

    private static double JaccardOf(string text, string query)
    {
        var textTrigrams = Trigrams(text);
        var queryTrigrams = Trigrams(query);
        var intersection = textTrigrams.Count(queryTrigrams.Contains);
        var union = textTrigrams.Count + queryTrigrams.Count - intersection;

        return union == 0 ? 0.0 : intersection / (double)union;
    }

    private static HashSet<string> Trigrams(string input)
    {
        var trigrams = new HashSet<string>();
        foreach (var word in input.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var padded = $"  {word} ";
            for (var i = 0; i <= padded.Length - 3; i++)
                trigrams.Add(padded.Substring(i, 3));
        }
        return trigrams;
    }
}
