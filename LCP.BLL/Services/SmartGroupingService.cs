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

    public async Task GroupVideosAsync()
    {
        var allEntries = await _repository.GetAllRawAsync();
        var changed = false;

        var dict = new Dictionary<string, List<VideoMetadata>>(StringComparer.OrdinalIgnoreCase);
        var defaultVideos = new List<VideoMetadata>();

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

        var multiKeys = dict
            .Where(kvp => kvp.Value.Count >= 2)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var kvp in dict.Where(kvp => kvp.Value.Count == 1).ToList())
        {
            var singleKey = kvp.Key;
            foreach (var multiKey in multiKeys)
            {
                if (singleKey.StartsWith(multiKey, StringComparison.OrdinalIgnoreCase))
                {
                    dict[multiKey].AddRange(kvp.Value);
                    dict.Remove(singleKey);
                    break;
                }
            }
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
                        changed = true;
                    }
                }
            }
        }

        foreach (var video in defaultVideos)
        {
            video.CollectionId = DefaultGroup;
            changed = true;
        }

        if (changed)
            await _repository.SaveAllAsync(allEntries);
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
