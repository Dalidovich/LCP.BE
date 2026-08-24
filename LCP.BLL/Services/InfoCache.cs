using LCP.BLL.Interfaces;

namespace LCP.BLL.Services;

public class InfoCache<T> : IInfoCache<T>
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyList<T>? _cached;

    public async Task<IReadOnlyList<T>> GetOrCreateAsync(Func<Task<IReadOnlyList<T>>> factory)
    {
        var cached = Volatile.Read(ref _cached);
        if (cached is not null) return cached;

        await _lock.WaitAsync();
        try
        {
            cached = Volatile.Read(ref _cached);
            if (cached is not null) return cached;

            var created = await factory();
            Volatile.Write(ref _cached, created);
            return created;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => Volatile.Write(ref _cached, null);
}
