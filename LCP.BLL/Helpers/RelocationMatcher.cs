using LCP.Domain;
using LCP.Domain.Entities;

namespace LCP.BLL.Helpers;

public sealed record Relocation(VideoMetadata Entry, string NewPath);

public static class RelocationMatcher
{
    public const double DurationToleranceSeconds = 1;

    public static List<Relocation> Match(
        IReadOnlyList<VideoMetadata> missingEntries,
        IReadOnlyList<string> untrackedPaths,
        IReadOnlyDictionary<string, double> probedDurations)
    {
        var relocations = new List<Relocation>();
        if (missingEntries.Count == 0 || untrackedPaths.Count == 0) return relocations;

        var pathsByFileName = untrackedPaths.ToLookup(
            p => Path.GetFileName(LibraryPath.Normalize(p)),
            StringComparer.OrdinalIgnoreCase);

        var entriesByFileName = missingEntries.GroupBy(
            e => Path.GetFileName(LibraryPath.Normalize(e.RelativePath)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in entriesByFileName)
        {
            var candidatePaths = pathsByFileName[group.Key].ToList();
            if (candidatePaths.Count == 0) continue;

            var candidateEntries = group.ToList();
            var matched = MatchByDuration(candidateEntries, candidatePaths, probedDurations);
            relocations.AddRange(matched);

            var remainingEntries = candidateEntries
                .Where(e => matched.All(m => !ReferenceEquals(m.Entry, e)))
                .ToList();
            var remainingPaths = candidatePaths
                .Where(p => matched.All(m => !string.Equals(m.NewPath, p, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (remainingEntries.Count == 1
                && remainingPaths.Count == 1
                && !DurationsConflict(remainingEntries[0].Duration, remainingPaths[0], probedDurations))
            {
                relocations.Add(new Relocation(remainingEntries[0], remainingPaths[0]));
            }
        }

        return relocations;
    }

    private static List<Relocation> MatchByDuration(
        List<VideoMetadata> candidateEntries,
        List<string> candidatePaths,
        IReadOnlyDictionary<string, double> probedDurations)
    {
        var matched = new List<Relocation>();

        foreach (var entry in candidateEntries)
        {
            if (entry.Duration <= 0) continue;

            var matchingPaths = candidatePaths
                .Where(p => DurationsMatch(entry.Duration, p, probedDurations))
                .ToList();
            if (matchingPaths.Count != 1) continue;

            var competingEntries = candidateEntries
                .Count(e => DurationsMatch(e.Duration, matchingPaths[0], probedDurations));
            if (competingEntries != 1) continue;

            matched.Add(new Relocation(entry, matchingPaths[0]));
        }

        return matched;
    }

    private static bool DurationsMatch(
        double entryDuration,
        string path,
        IReadOnlyDictionary<string, double> probedDurations)
    {
        if (entryDuration <= 0) return false;
        if (!probedDurations.TryGetValue(path, out var probed) || probed <= 0) return false;

        return Math.Abs(probed - entryDuration) <= DurationToleranceSeconds;
    }

    private static bool DurationsConflict(
        double entryDuration,
        string path,
        IReadOnlyDictionary<string, double> probedDurations)
    {
        if (entryDuration <= 0) return false;
        if (!probedDurations.TryGetValue(path, out var probed) || probed <= 0) return false;

        return Math.Abs(probed - entryDuration) > DurationToleranceSeconds;
    }
}
