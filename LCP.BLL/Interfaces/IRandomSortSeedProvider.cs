namespace LCP.BLL.Interfaces;

public interface IRandomSortSeedProvider
{
    int GetSeed(bool randomSortEnabled);
}
