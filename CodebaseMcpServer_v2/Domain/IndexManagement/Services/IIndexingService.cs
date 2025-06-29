using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;

namespace CodebaseMcpServer.Domain.IndexManagement.Services
{
    public interface IIndexingService
    {
        Task<IndexResult> IndexCodebaseAsync(CodebasePath path, IndexConfiguration config, CancellationToken cancellationToken = default);
        Task<bool> ValidateIndexIntegrityAsync(CollectionName collectionName, CancellationToken cancellationToken = default);
    }
}