using System.Text.Json;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.Extensions.Options;

namespace LCP.DAL.Repositories;

public class JsonVideoRepository : IVideoRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<VideoMetadata>? _cache;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public JsonVideoRepository(IOptions<LibrarySettings> settings)
    {
        _filePath = settings.Value.ResolveSystemFilePath(settings.Value.JsonFilePath);

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        if (!File.Exists(_filePath))
        {
            File.WriteAllText(_filePath, "[]");
        }
    }

    public async Task<List<VideoMetadata>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _cache ??= await LoadAsync();
            return [.. _cache];
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<VideoMetadata>> GetAllRawAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _cache ??= await LoadAsync();
            return [.. _cache];
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<VideoMetadata?> GetByIdAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            _cache ??= await LoadAsync();
            return _cache.FirstOrDefault(v => v.Id == id);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<(List<VideoMetadata> Items, int TotalCount)> GetPagedAsync(int page, int pageSize)
    {
        await _lock.WaitAsync();
        try
        {
            _cache ??= await LoadAsync();
            var query = _cache.ToList();
            var totalCount = query.Count;
            var items = query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            return (items, totalCount);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SoftDeleteAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            _cache ??= await LoadAsync();
            var video = _cache.FirstOrDefault(v => v.Id == id);
            if (video is not null)
            {
                video.IsDeleted = true;
                await SaveAsync(_cache);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAllAsync(List<VideoMetadata> videos)
    {
        await _lock.WaitAsync();
        try
        {
            _cache = videos;
            await SaveAsync(videos);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<VideoMetadata>> LoadAsync()
    {
        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<VideoMetadata>>(json) ?? [];
    }

    private async Task SaveAsync(List<VideoMetadata> data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
