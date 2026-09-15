using LCP.BLL.DTOs;
using LCP.DAL.Configuration;
using LCP.Domain.Entities;

namespace LCP.BLL.Helpers;

public static class CompilationPlanner
{
    private readonly record struct Interval(int Start, int End);

    private readonly record struct Window(int Offset, int Length);

    public static List<SelectedMoment> Plan(
        IReadOnlyList<WatchRecord> records,
        IReadOnlyDictionary<string, double> videoDurations,
        CompilationSettings settings)
    {
        var clusters = BuildClusters(records, videoDurations, settings.MergeGapSeconds);
        var candidates = BuildCandidates(clusters, settings);
        return Select(candidates, videoDurations, settings);
    }

    public static List<MomentCluster> BuildClusters(
        IReadOnlyList<WatchRecord> records,
        IReadOnlyDictionary<string, double> videoDurations,
        int mergeGapSeconds)
    {
        var momentsByVideo = new SortedDictionary<string, List<Interval>>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            if (!videoDurations.TryGetValue(record.VideoId, out var videoDuration)) continue;

            foreach (var segment in record.Segments)
            {
                var interval = ClipToVideo(segment, videoDuration);
                if (interval is null) continue;

                if (!momentsByVideo.TryGetValue(record.VideoId, out var moments))
                    momentsByVideo[record.VideoId] = moments = [];
                moments.Add(interval.Value);
            }
        }

        var clusters = new List<MomentCluster>();
        foreach (var (videoId, moments) in momentsByVideo)
        {
            moments.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.End.CompareTo(b.End));

            var batch = new List<Interval> { moments[0] };
            var batchEnd = moments[0].End;
            foreach (var moment in moments.Skip(1))
            {
                if (moment.Start <= batchEnd + mergeGapSeconds)
                {
                    batch.Add(moment);
                    batchEnd = Math.Max(batchEnd, moment.End);
                    continue;
                }

                clusters.Add(CreateCluster(videoId, batch));
                batch = [moment];
                batchEnd = moment.End;
            }
            clusters.Add(CreateCluster(videoId, batch));
        }

        return clusters;
    }

    public static List<CompilationCandidate> BuildCandidates(
        IReadOnlyList<MomentCluster> clusters,
        CompilationSettings settings)
    {
        var candidates = new List<CompilationCandidate>();

        foreach (var cluster in clusters)
        {
            var taken = new bool[cluster.Duration];
            for (var i = 0; i < settings.MaxCandidatesPerCluster; i++)
            {
                var window = FindBestWindow(cluster.Heat, taken, settings.MinMomentSeconds, settings.MaxMomentSeconds);
                if (window is null) break;

                var (offset, length) = window.Value;
                var heat = cluster.Heat[offset..(offset + length)];
                var popularity = Average(heat);

                candidates.Add(new CompilationCandidate(
                    cluster.VideoId, cluster.Start + offset, length, popularity, BaseScore(popularity), heat));

                Block(taken, offset - settings.MinDistanceSeconds, offset + length + settings.MinDistanceSeconds);
            }
        }

        return candidates;
    }

    public static List<SelectedMoment> Select(
        IReadOnlyList<CompilationCandidate> candidates,
        IReadOnlyDictionary<string, double> videoDurations,
        CompilationSettings settings)
    {
        var selected = new List<SelectedMoment>();
        var pool = candidates
            .OrderBy(c => c.VideoId, StringComparer.Ordinal)
            .ThenBy(c => c.Start)
            .ToList();
        var remaining = settings.MaxDurationSeconds;

        while (pool.Count > 0 && remaining >= settings.MinMomentSeconds)
        {
            var viable = new List<CompilationCandidate>();
            CompilationCandidate? bestCandidate = null;
            SelectedMoment? best = null;

            foreach (var candidate in pool)
            {
                var moment = Evaluate(candidate, selected, videoDurations, remaining, settings);
                if (moment is null) continue;

                viable.Add(candidate);
                if (best is null || IsBetter(moment, best))
                {
                    best = moment;
                    bestCandidate = candidate;
                }
            }

            if (best is null) break;

            viable.Remove(bestCandidate!);
            pool = viable;
            selected.Add(best);
            remaining -= best.Duration;
        }

        return selected;
    }

    private static SelectedMoment? Evaluate(
        CompilationCandidate candidate,
        IReadOnlyList<SelectedMoment> selected,
        IReadOnlyDictionary<string, double> videoDurations,
        double remaining,
        CompilationSettings settings)
    {
        var sameVideo = selected.Where(s => s.VideoId == candidate.VideoId).ToList();

        var blocked = new bool[candidate.Duration];
        foreach (var moment in sameVideo)
        {
            Block(blocked,
                moment.Start - settings.MinDistanceSeconds - candidate.Start,
                moment.Start + moment.Duration + settings.MinDistanceSeconds - candidate.Start);
        }

        var coverageLeft = MaxCoverage(videoDurations[candidate.VideoId], settings) - sameVideo.Sum(s => s.Duration);
        var maxLength = (int)Math.Floor(Math.Min(settings.MaxMomentSeconds, Math.Min(remaining, coverageLeft)));

        var window = FindBestWindow(candidate.Heat, blocked, settings.MinMomentSeconds, maxLength);
        if (window is null) return null;

        var (offset, length) = window.Value;
        var popularity = Average(candidate.Heat[offset..(offset + length)]);
        var freeRatio = (double)blocked.Count(b => !b) / candidate.Duration;
        var score = BaseScore(popularity) * freeRatio / (1 + settings.VideoRepeatPenalty * sameVideo.Count);

        return new SelectedMoment(candidate.VideoId, candidate.Start + offset, length, popularity, score);
    }

    private static double BaseScore(double popularity)
    {
        return popularity;
    }

    private static double MaxCoverage(double videoDuration, CompilationSettings settings)
    {
        return videoDuration > 0
            ? Math.Min(videoDuration * settings.MaxCoveragePercent, settings.MaxCoverageSeconds)
            : settings.MaxCoverageSeconds;
    }

    private static bool IsBetter(SelectedMoment moment, SelectedMoment best)
    {
        if (moment.Score != best.Score) return moment.Score > best.Score;
        if (moment.Popularity != best.Popularity) return moment.Popularity > best.Popularity;

        var byVideo = string.CompareOrdinal(moment.VideoId, best.VideoId);
        return byVideo != 0 ? byVideo < 0 : moment.Start < best.Start;
    }

    private static Interval? ClipToVideo(WatchSegment segment, double videoDuration)
    {
        var start = Math.Max(0, segment.Start);
        var end = segment.Start + segment.Duration;
        if (videoDuration > 0)
            end = Math.Min(end, (int)Math.Floor(videoDuration));

        return end > start ? new Interval(start, end) : null;
    }

    private static MomentCluster CreateCluster(string videoId, List<Interval> moments)
    {
        var start = moments.Min(m => m.Start);
        var end = moments.Max(m => m.End);

        var delta = new int[end - start + 1];
        foreach (var moment in moments)
        {
            delta[moment.Start - start]++;
            delta[moment.End - start]--;
        }

        var heat = new int[end - start];
        var running = 0;
        for (var i = 0; i < heat.Length; i++)
        {
            running += delta[i];
            heat[i] = running;
        }

        var watchedSeconds = moments.Sum(m => (long)(m.End - m.Start));
        return new MomentCluster(videoId, start, end, moments.Count, watchedSeconds, heat);
    }

    private static Window? FindBestWindow(int[] heat, bool[] blocked, int minLength, int maxLength)
    {
        if (maxLength < minLength || minLength < 1) return null;

        Window? best = null;
        var bestAverage = 0.0;

        var runStart = 0;
        while (runStart < heat.Length)
        {
            if (blocked[runStart])
            {
                runStart++;
                continue;
            }

            var runEnd = runStart;
            while (runEnd < heat.Length && !blocked[runEnd]) runEnd++;

            var length = Math.Min(maxLength, runEnd - runStart);
            if (length >= minLength)
            {
                var sum = 0L;
                for (var i = runStart; i < runStart + length; i++) sum += heat[i];

                var bestOffset = runStart;
                var bestSum = sum;
                for (var offset = runStart + 1; offset + length <= runEnd; offset++)
                {
                    sum += heat[offset + length - 1] - heat[offset - 1];
                    if (sum > bestSum)
                    {
                        bestSum = sum;
                        bestOffset = offset;
                    }
                }

                var average = (double)bestSum / length;
                if (bestSum > 0 && (best is null || average > bestAverage || (average == bestAverage && length > best.Value.Length)))
                {
                    best = new Window(bestOffset, length);
                    bestAverage = average;
                }
            }

            runStart = runEnd;
        }

        return best is null ? null : TrimColdEdges(heat, best.Value, minLength);
    }

    private static Window TrimColdEdges(int[] heat, Window window, int minLength)
    {
        var start = window.Offset;
        var end = window.Offset + window.Length;

        while (end - start > minLength && heat[start] == 0) start++;
        while (end - start > minLength && heat[end - 1] == 0) end--;

        return new Window(start, end - start);
    }

    private static void Block(bool[] blocked, int from, int to)
    {
        for (var i = Math.Max(0, from); i < Math.Min(blocked.Length, to); i++)
            blocked[i] = true;
    }

    private static double Average(int[] heat)
    {
        return heat.Length == 0 ? 0 : (double)heat.Sum() / heat.Length;
    }
}
