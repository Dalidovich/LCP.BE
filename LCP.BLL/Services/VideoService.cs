using LCP.BLL.DTOs;
using LCP.BLL.Interfaces;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.Extensions.Options;

namespace LCP.BLL.Services;

public class VideoService : IVideoService
{
    private readonly IVideoRepository _repository;
    private readonly IThumbnailService _thumbnailService;
    private readonly IPreviewService _previewService;
    private readonly ISettingsRepository _settingsRepository;
    private readonly string _libraryRootPath;

    public VideoService(
        IVideoRepository repository,
        IThumbnailService thumbnailService,
        IPreviewService previewService,
        ISettingsRepository settingsRepository,
        IOptions<LibrarySettings> settings)
    {
        _repository = repository;
        _thumbnailService = thumbnailService;
        _previewService = previewService;
        _settingsRepository = settingsRepository;
        _libraryRootPath = settings.Value.LibraryRootPath;
    }

    public async Task<List<VideoDto>> GetAllAsync()
    {
        var videos = await _repository.GetAllRawAsync();
        var ordered = await OrderIfStatisticsModeAsync(videos);
        return ordered.Select(MapToDto).ToList();
    }

    public async Task<PagedResult<VideoDto>> GetPagedAsync(int page, int pageSize)
    {
        var videos = await _repository.GetAllRawAsync();
        var ordered = await OrderIfStatisticsModeAsync(videos);
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

    public async Task<List<VideoDto>> GetByCollectionIdAsync(string collectionId)
    {
        var videos = await _repository.GetByCollectionIdAsync(collectionId);
        return videos.Select(MapToDto).ToList();
    }

    public async Task<List<CollectionDto>> GetAllCollectionIdsAsync()
    {
        var collections = await _repository.GetAllCollectionIdsAsync();
        return collections.Select(c => new CollectionDto { Id = c.Id, Count = c.Count }).ToList();
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
            entry.Tags = request.Tags;
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

    public async Task<bool> SoftDeleteAsync(string id)
    {
        var video = await _repository.GetByIdAsync(id);
        if (video is null) return false;

        await _repository.SoftDeleteAsync(id);
        return true;
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

    private async Task<List<VideoMetadata>> OrderIfStatisticsModeAsync(List<VideoMetadata> videos)
    {
        var settings = await _settingsRepository.GetAsync();
        if (settings is not null && settings.StatisticsMode)
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
        IsDeleted = v.IsDeleted,
        ThumbnailTimecode = v.ThumbnailTimecode,
        Duration = v.Duration,
        LastTimeWatched = v.LastTimeWatched,
        PreviewSlices = [.. v.PreviewSlices]
    };
}
