using System.Text.Json;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using Microsoft.Extensions.Options;

namespace LCP.DAL.Repositories;

public class JsonProductionInfoRepository : IProductionInfoRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<string>? _cache;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public JsonProductionInfoRepository(IOptions<LibrarySettings> settings)
    {
        _filePath = settings.Value.ResolveSystemFilePath(LibrarySettings.ProductionInfoFileName);
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

    public async Task<List<string>> GetAllAsync()
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

    public async Task AddAsync(string studio)
    {
        await _lock.WaitAsync();
        try
        {
            _cache ??= await LoadAsync();
            if (_cache.Contains(studio, StringComparer.OrdinalIgnoreCase)) return;

            _cache.Add(studio);
            _cache.Sort(StringComparer.OrdinalIgnoreCase);
            await SaveAsync(_cache);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAsync(string studio)
    {
        await _lock.WaitAsync();
        try
        {
            _cache ??= await LoadAsync();
            var removed = _cache.RemoveAll(t => t.Equals(studio, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                await SaveAsync(_cache);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<string>> LoadAsync()
    {
        if (string.IsNullOrEmpty(_filePath)) return [];

        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<string>>(json) ?? [];
    }

    private async Task SaveAsync(List<string> data)
    {
        if (string.IsNullOrEmpty(_filePath)) return;

        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
