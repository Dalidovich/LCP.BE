using LCP.Domain.Entities;

namespace LCP.BLL.Helpers;

public static class SearchHelper
{
    private const double MinScore = 0.2;

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

    public static double TrigramSimilarity(string a, string b)
    {
        var normA = a.ToLowerInvariant().Trim();
        var normB = b.ToLowerInvariant().Trim();

        if (normA.Length < 3 || normB.Length < 3)
            return normA.Contains(normB) || normB.Contains(normA) ? 1.0 : 0.0;

        var setA = GetTrigrams(a);
        var setB = GetTrigrams(b);

        if (setA.Count == 0 && setB.Count == 0) return 1.0;
        if (setA.Count == 0 || setB.Count == 0) return 0.0;

        var intersection = setA.Count(t => setB.Contains(t));
        var min = Math.Min(setA.Count, setB.Count);

        return intersection / (double)min;
    }

    public static bool IsMatch(VideoMetadata video, string query)
    {
        return ScoreVideo(video, query) >= MinScore;
    }

    private static HashSet<string> GetTrigrams(string input)
    {
        var normalized = input.ToLowerInvariant().Trim();
        if (string.IsNullOrEmpty(normalized))
            return [];

        if (normalized.Length < 3)
        {
            var set = new HashSet<string> { normalized };
            return set;
        }

        var trigrams = new HashSet<string>();
        for (var i = 0; i <= normalized.Length - 3; i++)
        {
            trigrams.Add(normalized.Substring(i, 3));
        }

        return trigrams;
    }
}
