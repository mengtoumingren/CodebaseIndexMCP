using System.Collections.Generic;
using System.Threading.Tasks;
using CodebaseMcpServer.Domain.Entities;

namespace CodebaseMcpServer.Domain.Repositories
{
    public interface IIndexLibraryRepository
    {
        Task<IndexLibrary?> GetByIdAsync(int id);
        Task<IEnumerable<IndexLibrary>> GetAllAsync();
        Task AddAsync(IndexLibrary library);
        Task UpdateAsync(IndexLibrary library);
        Task DeleteAsync(int id);
    }
}
