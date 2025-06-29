using System.Threading.Tasks;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;
using CodebaseMcpServer.Domain.SemanticSearch.ValueObjects;

namespace CodebaseMcpServer.Domain.SemanticSearch.Services
{
    public interface ISemanticSearchService
    {
        Task<SearchResult> SearchAsync(SearchQuery query, CollectionName collection);
        Task<bool> IsCollectionReadyAsync(CollectionName collection);
    }
}