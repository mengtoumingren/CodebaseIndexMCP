using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Domain.IndexManagement.Aggregates;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;

namespace CodebaseMcpServer.Domain.IndexManagement.Repositories
{
    public interface IIndexLibraryRepository
    {
        Task<IndexLibrary?> GetByIdAsync(IndexLibraryId id, CancellationToken cancellationToken = default);
        Task<IndexLibrary?> GetByCodebasePathAsync(CodebasePath codebasePath, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<IndexLibrary>> GetAllAsync(CancellationToken cancellationToken = default);
        Task AddAsync(IndexLibrary library, CancellationToken cancellationToken = default);
        Task UpdateAsync(IndexLibrary library, CancellationToken cancellationToken = default);
        Task DeleteAsync(IndexLibraryId id, CancellationToken cancellationToken = default);
        Task<int> GetNextIdAsync(CancellationToken cancellationToken = default);
        Task SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}