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
    private IReadOnlyList<VideoMetadata>? _cache;

    public JsonVideoRepository(IOptions<LibrarySettings> settings)
    {
        _filePath = settings.Value.ResolveSystemFilePath(LibrarySettings.JsonFileName);
        if (string.IsNullOrEmpty(_filePath)) return;

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

    public async Task<IReadOnlyList<VideoMetadata>> GetSnapshotAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return _cache ??= await LoadAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<VideoMetadata>> GetByCollectionIdAsync(string collectionId)
    {
        var snapshot = await GetSnapshotAsync();
        return [.. snapshot.Where(v => v.CollectionId == collectionId)];
    }

    public async Task<List<(string Id, int Count)>> GetAllCollectionIdsAsync()
    {
        var snapshot = await GetSnapshotAsync();
        return snapshot
            .Where(v => v.CollectionId is not null)
            .GroupBy(v => v.CollectionId!)
            .Select(g => (Id: g.Key, Count: g.Count()))
            .ToList();
    }

    public async Task<VideoMetadata?> GetByIdAsync(string id)
    {
        var snapshot = await GetSnapshotAsync();
        return snapshot.FirstOrDefault(v => v.Id == id);
    }

    public async Task SaveAllAsync(List<VideoMetadata> videos)
    {
        await _lock.WaitAsync();
        try
        {
            await PublishAsync([.. videos.Select(v => v.Clone())]);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<T> MutateAsync<T>(Func<List<VideoMetadata>, (bool Changed, T Result)> mutation)
    {
        await _lock.WaitAsync();
        try
        {
            var current = _cache ??= await LoadAsync();
            var working = current.Select(v => v.Clone()).ToList();

            var (changed, result) = mutation(working);
            if (changed)
            {
                await PublishAsync(working);
            }
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task InvalidateCacheAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _cache = null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task PublishAsync(List<VideoMetadata> videos)
    {
        try
        {
            await SaveAsync(videos);
        }
        catch
        {
            _cache = null;
            throw;
        }
        _cache = videos;
    }

    private async Task<List<VideoMetadata>> LoadAsync()
    {
        if (string.IsNullOrEmpty(_filePath)) return [];

        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<VideoMetadata>>(json, JsonStore.Options) ?? [];
    }

    private async Task SaveAsync(List<VideoMetadata> data)
    {
        if (string.IsNullOrEmpty(_filePath)) return;

        var json = JsonSerializer.Serialize(data, JsonStore.Options);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
