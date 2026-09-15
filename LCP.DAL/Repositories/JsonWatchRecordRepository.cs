using System.Text.Json;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.Extensions.Options;

namespace LCP.DAL.Repositories;

public class JsonWatchRecordRepository : IWatchRecordRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonWatchRecordRepository(IOptions<LibrarySettings> settings)
    {
        _filePath = settings.Value.ResolveSystemFilePath(LibrarySettings.MostWatchedFileName);
    }

    public async Task AppendAsync(WatchRecord record)
    {
        if (string.IsNullOrEmpty(_filePath)) return;

        await _lock.WaitAsync();
        try
        {
            var records = await LoadAsync();
            records.Add(record);
            await SaveAsync(records);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<WatchRecord>> GetAllAsync()
    {
        if (string.IsNullOrEmpty(_filePath)) return [];

        await _lock.WaitAsync();
        try
        {
            return await LoadAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<WatchRecord>> LoadAsync()
    {
        if (!File.Exists(_filePath)) return [];

        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<List<WatchRecord>>(json, JsonStore.Options) ?? [];
    }

    private async Task SaveAsync(List<WatchRecord> data)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(data, JsonStore.Options);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
