using LCP.BLL.Interfaces;
using LCP.DAL.Interfaces;

namespace LCP.BLL.Services;

public class TagService : ITagService
{
    private readonly ITagRepository _repository;

    public TagService(ITagRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<string>> GetAllAsync()
    {
        return await _repository.GetAllAsync();
    }

    public async Task AddAsync(string tag)
    {
        await _repository.AddAsync(tag);
    }

    public async Task<bool> RemoveAsync(string tag)
    {
        var tags = await _repository.GetAllAsync();
        if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return false;

        await _repository.RemoveAsync(tag);
        return true;
    }
}
