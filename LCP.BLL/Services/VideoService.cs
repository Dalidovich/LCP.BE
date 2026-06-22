using LCP.BLL.DTOs;
using LCP.BLL.Helpers;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.Extensions.Options;

namespace LCP.BLL.Services;

public class VideoService : IVideoService
{
    private readonly IVideoRepository _repository;
    private readonly ITagRepository _tagRepository;
    private readonly ITagService _tagService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IPreviewService _previewService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly string _libraryRootPath;
    private static int? _randomSortSeed;
    private static bool _randomSortWasEnabled;

    public VideoService(
        IVideoRepository repository,
        ITagRepository tagRepository,
        ITagService tagService,
        IThumbnailService thumbnailService,
        IPreviewService previewService,
        ISettingsRepository settingsRepository,
        IOptions<LibrarySettings> settings)
    {
        _repository = repository;
        _tagRepository = tagRepository;
        _tagService = tagService;
        _thumbnailService = thumbnailService;
        _previewService = previewService;
        _settingsRepository = settingsRepository;
        _libraryRootPath = settings.Value.LibraryRootPath;
    }

    public async Task<List<VideoDto>> GetAllAsync(string? search = null)
    {
        var videos = await _repository.GetAllRawAsync();
        List<VideoMetadata> ordered;

        if (!string.IsNullOrWhiteSpace(search))
        {
            ordered = videos
                .Select(v => (Video: v, Score: SearchHelper.ScoreVideo(v, search)))
                .Where(x => x.Score >= 0.2)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Video)
                .ToList();
        }
        else
        {
            ordered = await ApplyOrderingAsync(videos);
        }

        ordered = await FilterByTypeAsync(ordered);
        return ordered.Select(MapToDto).ToList();
    }

    public async Task<PagedResult<VideoDto>> GetPagedAsync(int page, int pageSize, List<string>? tags = null, string? search = null)
    {
        var videos = await _repository.GetAllRawAsync();
        List<VideoMetadata> ordered;

        if (!string.IsNullOrWhiteSpace(search))
        {
            ordered = videos
                .Select(v => (Video: v, Score: SearchHelper.ScoreVideo(v, search)))
                .Where(x => x.Score >= 0.2)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Video)
                .ToList();
        }
        else
        {
            ordered = await ApplyOrderingAsync(videos);
        }

        ordered = await FilterByTypeAsync(ordered);

        if (tags is { Count: > 0 })
        {
            var tagSet = tags.Select(t => t.ToLowerInvariant()).ToHashSet();
            ordered = ordered
                .Where(v => v.Tags.Any(t => tagSet.Contains(t.ToLowerInvariant())))
                .OrderByDescending(v => v.Tags.Count(t => tagSet.Contains(t.ToLowerInvariant())))
                .ToList();
        }

        var totalCount = ordered.Count;
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToDto)
            .ToList();
        return new PagedResult<VideoDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<VideoDto?> GetByIdAsync(string id)
    {
        var video = await _repository.GetByIdAsync(id);
        return video is null ? null : MapToDto(video);
    }

    public async Task<PagedResult<VideoDto>> GetByCollectionIdAsync(string collectionId, int page = 1, int pageSize = 20, string? search = null)
    {
        var videos = await _repository.GetByCollectionIdAsync(collectionId);
        videos = await FilterByTypeAsync(videos);

        List<VideoMetadata> ordered;
        if (!string.IsNullOrWhiteSpace(search))
        {
            ordered = videos
                .Select(v => (Video: v, Score: SearchHelper.ScoreVideo(v, search)))
                .Where(x => x.Score >= 0.2)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Video)
                .ToList();
        }
        else
        {
            ordered = await ApplyOrderingAsync(videos);
        }

        var totalCount = ordered.Count;
        var items = ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToDto)
            .ToList();
        return new PagedResult<VideoDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<PagedResult<CollectionDto>> GetAllCollectionIdsAsync(int page = 1, int pageSize = 20, string? search = null)
    {
        var videos = await _repository.GetAllRawAsync();
        var filtered = await FilterByTypeAsync(videos);
        var collections = filtered
            .GroupBy(v => v.CollectionId ?? "default")
            .Select(g => (Id: g.Key, Count: g.Count()))
            .OrderBy(c => c.Id)
            .ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var query = search.ToLowerInvariant();
            collections = collections
                .Where(c => c.Id.ToLowerInvariant().Contains(query))
                .ToList();
        }

        var totalCount = collections.Count;
        var items = collections
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CollectionDto { Id = c.Id, Count = c.Count })
            .ToList();
        return new PagedResult<CollectionDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<VideoDto?> UpdateAsync(string id, UpdateVideoRequest request)
    {
        var allEntries = await _repository.GetAllRawAsync();
        var entry = allEntries.FirstOrDefault(v => v.Id == id);
        if (entry is null) return null;

        if (request.NameEn is not null)
            entry.NameEn = request.NameEn;
        if (request.NameLocal is not null)
            entry.NameLocal = request.NameLocal;
        if (request.CollectionId is not null)
            entry.CollectionId = request.CollectionId;
        if (request.EpisodeNumber is not null)
            entry.EpisodeNumber = request.EpisodeNumber.Value;
        if (request.Type is not null)
            entry.Type = request.Type.Value;
        if (request.Tags is not null)
        {
            entry.Tags = request.Tags;
            _tagService.InvalidateInfoCache();
        }
        if (request.ThumbnailTimecode is not null)
        {
            entry.ThumbnailTimecode = request.ThumbnailTimecode.Value;
            _thumbnailService.InvalidateCache(id);
        }
        if (request.LastTimeWatched is not null)
            entry.LastTimeWatched = request.LastTimeWatched;

        await _repository.SaveAllAsync(allEntries);
        return MapToDto(entry);
    }

    public async Task<string?> ResolveFilePathAsync(string id)
    {
        var video = await _repository.GetByIdAsync(id);
        if (video is null) return null;

        var fullPath = Path.Combine(_libraryRootPath, video.RelativePath);
        return File.Exists(fullPath) ? fullPath : null;
    }

    public async Task<VideoDto?> RegenerateSlicesAsync(string id)
    {
        var allEntries = await _repository.GetAllRawAsync();
        var entry = allEntries.FirstOrDefault(v => v.Id == id);
        if (entry is null) return null;

        entry.PreviewSlices = PreviewSlice.CalculateRandomSlices(entry.Duration);
        _previewService.InvalidateCache(id);
        await _repository.SaveAllAsync(allEntries);
        return MapToDto(entry);
    }

    public async Task<VideoDto?> AddVideoFileAsync(string fileName, Stream content)
    {
        var ext = Path.GetExtension(fileName);
        var rawName = Path.GetFileNameWithoutExtension(fileName);
        var name = rawName;
        var counter = 1;
        while (File.Exists(Path.Combine(_libraryRootPath, $"{name}{ext}")))
            name = $"{rawName} ({counter++})";

        var relativePath = $"{name}{ext}";
        var fullPath = Path.Combine(_libraryRootPath, relativePath);

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await using (var fs = new FileStream(fullPath, FileMode.CreateNew))
        {
            await content.CopyToAsync(fs);
        }

        var duration = FFProbeHelper.ProbeDuration(fullPath);

        var entry = new VideoMetadata
        {
            Id = Guid.NewGuid().ToString(),
            RelativePath = relativePath,
            SystemName = rawName,
            Duration = duration,
            PreviewSlices = PreviewSlice.CalculateSlices(duration)
        };

        var allEntries = await _repository.GetAllRawAsync();
        allEntries.Add(entry);
        await _repository.SaveAllAsync(allEntries);

        return MapToDto(entry);
    }

    public async Task<VideoDto?> GetRandomAsync()
    {
        var videos = await _repository.GetAllRawAsync();
        var filtered = await FilterByTypeAsync(videos);
        if (filtered.Count == 0) return null;
        var idx = Random.Shared.Next(filtered.Count);
        return MapToDto(filtered[idx]);
    }

    public async Task<PagedResult<VideoDto>> GetSimilarAsync(string id, int page = 1, int pageSize = 20)
    {
        var source = await _repository.GetByIdAsync(id);
        if (source is null) return new PagedResult<VideoDto> { Page = page, PageSize = pageSize };

        var tags = source.Tags.Select(t => t.ToLowerInvariant()).ToList();
        if (tags.Count == 0) return new PagedResult<VideoDto> { Page = page, PageSize = pageSize };

        var allVideos = await _repository.GetAllRawAsync();
        var filtered = allVideos.Where(v => v.Id != id).ToList();
        filtered = await FilterByTypeAsync(filtered);

        var scored = ScoreAndInterleave(tags, filtered);
        var totalCount = scored.Count;
        var items = scored
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        return new PagedResult<VideoDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    private List<VideoDto> ScoreAndInterleave(List<string> queryTags, List<VideoMetadata> videos)
    {
        var querySet = queryTags.ToHashSet();
        var maxTagCount = queryTags.Count;

        var scored = new List<(VideoMetadata Video, int Count, double Percent)>();
        foreach (var video in videos)
        {
            var matchCount = video.Tags.Count(t => querySet.Contains(t.ToLowerInvariant()));
            if (matchCount == 0) continue;

            var videoTagCount = video.Tags.Count;
            var percent = matchCount / (double)Math.Max(maxTagCount, videoTagCount);
            scored.Add((video, matchCount, percent));
        }

        var byCount = scored.OrderByDescending(s => s.Count).ThenByDescending(s => s.Percent).ToList();
        var byPercent = scored.OrderByDescending(s => s.Percent).ThenByDescending(s => s.Count).ToList();

        var seen = new HashSet<string>();
        var result = new List<VideoDto>();
        var max = Math.Max(byCount.Count, byPercent.Count);

        for (var i = 0; i < max; i++)
        {
            if (i < byCount.Count && seen.Add(byCount[i].Video.Id))
                result.Add(MapToDto(byCount[i].Video));
            if (i < byPercent.Count && seen.Add(byPercent[i].Video.Id))
                result.Add(MapToDto(byPercent[i].Video));
        }

        return result;
    }

    private async Task<List<VideoMetadata>> FilterByTypeAsync(List<VideoMetadata> videos)
    {
        var settings = await _settingsRepository.GetAsync();
        if (settings?.VideoTypeFilter is not { Count: > 0 })
            return videos;

        var filterSet = settings.VideoTypeFilter.ToHashSet();
        return videos.Where(v => filterSet.Contains(v.Type)).ToList();
    }

    private async Task<List<VideoMetadata>> ApplyOrderingAsync(List<VideoMetadata> videos)
    {
        var settings = await _settingsRepository.GetAsync();
        if (settings is null) return videos;

        if (settings.RandomSort)
        {
            if (!_randomSortWasEnabled || _randomSortSeed is null)
            {
                _randomSortSeed = Random.Shared.Next();
            }
            _randomSortWasEnabled = true;

            var rng = new Random(_randomSortSeed.Value);
            return videos.OrderBy(_ => rng.Next()).ToList();
        }

        _randomSortWasEnabled = false;

        if (settings.StatisticsMode)
        {
            return videos.OrderBy(v => v.LastTimeWatched ?? DateTime.MinValue).ToList();
        }

        return videos;
    }

    private static VideoDto MapToDto(VideoMetadata v) => new()
    {
        Id = v.Id,
        RelativePath = v.RelativePath,
        SystemName = v.SystemName,
        NameEn = v.NameEn,
        NameLocal = v.NameLocal,
        CollectionId = v.CollectionId,
        EpisodeNumber = v.EpisodeNumber,
        Type = v.Type,
        Tags = [.. v.Tags],
        ThumbnailTimecode = v.ThumbnailTimecode,
        Duration = v.Duration,
        LastTimeWatched = v.LastTimeWatched,
        PreviewSlices = [.. v.PreviewSlices]
    };
}
