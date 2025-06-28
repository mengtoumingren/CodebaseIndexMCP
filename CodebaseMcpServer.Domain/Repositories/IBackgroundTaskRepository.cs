using System.Collections.Generic;
using System.Threading.Tasks;
using CodebaseMcpServer.Domain.Entities;

namespace CodebaseMcpServer.Domain.Repositories
{
    public interface IBackgroundTaskRepository
    {
        Task<BackgroundTask?> GetByIdAsync(int id);
        Task<IEnumerable<BackgroundTask>> GetAllAsync();
        Task AddAsync(BackgroundTask task);
        Task UpdateAsync(BackgroundTask task);
        Task DeleteAsync(int id);
    }
}
