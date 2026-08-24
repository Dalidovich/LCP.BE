namespace LCP.BLL.Interfaces;

public interface IMediaWarmupService
{
    void QueueWarm(IReadOnlyList<string> videoIds, CancellationToken cancellationToken);
}
