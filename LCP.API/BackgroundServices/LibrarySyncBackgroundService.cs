using LCP.BLL.Interfaces;

namespace LCP.API.BackgroundServices;

public class LibrarySyncBackgroundService : IHostedService
{
    private readonly ILibrarySyncService _syncService;

    public LibrarySyncBackgroundService(ILibrarySyncService syncService)
    {
        _syncService = syncService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _syncService.SyncAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
