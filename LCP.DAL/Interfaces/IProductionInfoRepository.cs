namespace LCP.DAL.Interfaces;

public interface IProductionInfoRepository
{
    Task<List<string>> GetAllAsync();
    Task AddAsync(string studio);
    Task RemoveAsync(string studio);
}
