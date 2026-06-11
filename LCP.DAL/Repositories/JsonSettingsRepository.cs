using System.Text.Json;
using LCP.DAL.Configuration;
using LCP.DAL.Interfaces;
using LCP.Domain.Entities;
using Microsoft.Extensions.Options;

namespace LCP.DAL.Repositories;

public class JsonSettingsRepository : ISettingsRepository
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SiteSettings? _cache;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public JsonSettingsRepository(IOptions<LibrarySettings> settings)
    {
        _filePath = settings.Value.ResolveSystemFilePath(settings.Value.SettingsFilePath);

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public async Task<SiteSettings?> GetAsync()
    {
        await _lock.WaitAsync();
        try
        {
            _cache ??= await LoadAsync();
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(SiteSettings settings)
    {
        await _lock.WaitAsync();
        try
        {
            _cache = settings;
            await SaveAsync(settings);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<SiteSettings> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new SiteSettings();
        }

        var json = await File.ReadAllTextAsync(_filePath);
        return JsonSerializer.Deserialize<SiteSettings>(json) ?? new SiteSettings();
    }

    private async Task SaveAsync(SiteSettings data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json);
    }
}
