using LCP.BLL.Interfaces;

namespace LCP.BLL.Services;

public class RandomSortSeedProvider : IRandomSortSeedProvider
{
    private readonly Lock _gate = new();
    private int? _seed;
    private bool _wasEnabled;

    public int GetSeed(bool randomSortEnabled)
    {
        lock (_gate)
        {
            if (!randomSortEnabled)
            {
                _wasEnabled = false;
                return _seed ?? 0;
            }

            if (!_wasEnabled || _seed is null)
                _seed = Random.Shared.Next();

            _wasEnabled = true;
            return _seed.Value;
        }
    }
}
