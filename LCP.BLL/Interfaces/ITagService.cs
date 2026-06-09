namespace LCP.BLL.Interfaces;

public interface ITagService
{
    Task<List<string>> GetAllAsync();
    Task AddAsync(string tag);
    Task<bool> RemoveAsync(string tag);
}
