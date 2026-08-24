using LCP.Domain.Entities;

namespace LCP.BLL.Helpers;

public static class SearchHelper
{
    public const double MinScore = 0.25;

    private const double PrefixScore = 0.8;
    private const double ContainmentScore = 0.6;
    private const int FullConfidenceQueryLength = 3;

    public static double ScoreVideo(VideoMetadata video, string query)
    {
        var fields = new[]
        {
            video.SystemName,
            video.NameEn,
            video.NameLocal
        };

        var best = 0.0;
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field)) continue;
            var score = TrigramSimilarity(field, query);
            if (score > best)
                best = score;
        }

        return best;
    }

    public static double TrigramSimilarity(string text, string query)
    {
        var normText = Normalize(text);
        var normQuery = Normalize(query);

        if (normText.Length == 0 || normQuery.Length == 0)
            return normText.Length == normQuery.Length ? 1.0 : 0.0;

        var affinity = AffinityScore(normText, normQuery);

        var textTrigrams = GetTrigrams(normText);
        var queryTrigrams = GetTrigrams(normQuery);

        if (textTrigrams.Count == 0 || queryTrigrams.Count == 0)
            return affinity;

        var intersection = textTrigrams.Count(queryTrigrams.Contains);
        var union = textTrigrams.Count + queryTrigrams.Count - intersection;
        var jaccard = union == 0 ? 0.0 : intersection / (double)union;

        return jaccard + (1.0 - jaccard) * affinity;
    }

    public static bool IsMatch(VideoMetadata video, string query)
    {
        return ScoreVideo(video, query) >= MinScore;
    }

    private static double AffinityScore(string normText, string normQuery)
    {
        if (normQuery.Length > normText.Length)
            return 0.0;

        var confidence = Math.Min(1.0, normQuery.Length / (double)FullConfidenceQueryLength);

        if (StartsAnyWord(normText, normQuery))
            return PrefixScore * confidence;

        if (normText.Contains(normQuery, StringComparison.Ordinal))
            return ContainmentScore * confidence;

        return 0.0;
    }

    private static bool StartsAnyWord(string text, string prefix)
    {
        if (text.StartsWith(prefix, StringComparison.Ordinal))
            return true;

        var index = text.IndexOf(' ');
        while (index >= 0 && index + 1 + prefix.Length <= text.Length)
        {
            if (string.CompareOrdinal(text, index + 1, prefix, 0, prefix.Length) == 0)
                return true;
            index = text.IndexOf(' ', index + 1);
        }

        return false;
    }

    private static string Normalize(string input)
    {
        return string.Join(' ', input.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static HashSet<string> GetTrigrams(string normalized)
    {
        var trigrams = new HashSet<string>();

        foreach (var word in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var padded = $"  {word} ";
            for (var i = 0; i <= padded.Length - 3; i++)
            {
                trigrams.Add(padded.Substring(i, 3));
            }
        }

        return trigrams;
    }
}
