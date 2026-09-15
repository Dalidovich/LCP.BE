using LCP.Domain.Entities;

namespace LCP.DAL.Interfaces;

public interface IWatchRecordRepository
{
    Task AppendAsync(WatchRecord record);
    Task<List<WatchRecord>> GetAllAsync();
}
