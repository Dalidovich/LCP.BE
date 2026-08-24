namespace LCP.BLL.Interfaces;

public interface IInfoCache<T>
{
    Task<IReadOnlyList<T>> GetOrCreateAsync(Func<Task<IReadOnlyList<T>>> factory);
    void Invalidate();
}
