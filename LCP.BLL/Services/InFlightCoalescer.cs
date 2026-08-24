using System.Collections.Concurrent;

namespace LCP.BLL.Services;

internal sealed class InFlightCoalescer<T> where T : class
{
    private readonly ConcurrentDictionary<string, Lazy<Task<T?>>> _inFlight = new();

    public Task<T?> RunAsync(string key, Func<Task<T?>> factory)
    {
        var lazy = _inFlight.GetOrAdd(key, k => new Lazy<Task<T?>>(
            () => RunAndReleaseAsync(k, factory),
            LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value;
    }

    private async Task<T?> RunAndReleaseAsync(string key, Func<Task<T?>> factory)
    {
        try
        {
            return await factory().ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }
}
