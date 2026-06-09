namespace LCP.DAL.Interfaces;

public interface ITagRepository
{
    Task<List<string>> GetAllAsync();
    Task AddAsync(string tag);
    Task RemoveAsync(string tag);
}
