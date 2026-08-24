using System.Text.RegularExpressions;
using LCP.BLL.Interfaces;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;

namespace LCP.BLL.Services;

public class SmartGroupingService : ISmartGroupingService
{
    private readonly IVideoRepository _repository;

    public SmartGroupingService(IVideoRepository repository)
    {
        _repository = repository;
    }

    private const string DefaultGroup = "default";

    public Task GroupVideosAsync() =>
        _repository.MutateAsync<object?>(allEntries => (Group(allEntries), null));

    private static bool Group(List<VideoMetadata> allEntries)
    {
        var changed = false;

        var dict = new Dictionary<string, List<VideoMetadata>>(StringComparer.OrdinalIgnoreCase);
        var defaultVideos = new HashSet<VideoMetadata>();

        foreach (var video in allEntries)
        {
            if (!string.IsNullOrEmpty(video.CollectionId))
                continue;

            var key = CleanName(video.SystemName);
            if (string.IsNullOrWhiteSpace(key))
            {
                if (video.CollectionId != DefaultGroup)
                {
                    defaultVideos.Add(video);
                }
                continue;
            }

            if (!dict.TryGetValue(key, out var list))
            {
                list = [];
                dict[key] = list;
            }
            list.Add(video);
        }

        var multiKeysByLongestFirst = dict
            .Where(kvp => kvp.Value.Count >= 2)
            .Select(kvp => kvp.Key)
            .OrderByDescending(key => key.Length)
            .ThenBy(key => key, StringComparer.Ordinal)
            .ToList();

        var absorptions = dict
            .Where(kvp => kvp.Value.Count == 1)
            .Select(kvp => kvp.Key)
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(singleKey => new
            {
                SingleKey = singleKey,
                TargetKey = multiKeysByLongestFirst.FirstOrDefault(multiKey =>
                    singleKey.StartsWith(multiKey, StringComparison.OrdinalIgnoreCase))
            })
            .Where(absorption => absorption.TargetKey is not null)
            .ToList();

        foreach (var absorption in absorptions)
        {
            dict[absorption.TargetKey!].AddRange(dict[absorption.SingleKey]);
            dict.Remove(absorption.SingleKey);
        }

        foreach (var (key, videos) in dict)
        {
            if (videos.Count >= 2)
            {
                foreach (var video in videos)
                {
                    video.CollectionId = key;
                    changed = true;
                }
            }
            else
            {
                foreach (var video in videos)
                {
                    if (video.CollectionId != DefaultGroup)
                    {
                        defaultVideos.Add(video);
                    }
                }
            }
        }

        foreach (var video in defaultVideos)
        {
            video.CollectionId = DefaultGroup;
        }

        changed |= defaultVideos.Count > 0;

        return changed;
    }

    private static string CleanName(string systemName)
    {
        if (string.IsNullOrWhiteSpace(systemName))
            return string.Empty;

        var name = systemName.ToLowerInvariant();
        var original = name;

        name = Regex.Replace(name, @"\s*\bep\s*\d*\b\s*", " ", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s+\d+$", "");
        name = Regex.Replace(name, @"\s+", " ");
        name = name.Trim();

        return string.IsNullOrWhiteSpace(name) ? original.Trim() : name;
    }
}
