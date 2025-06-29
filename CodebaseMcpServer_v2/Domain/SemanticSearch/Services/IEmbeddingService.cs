using System.Collections.Generic;
using System.Threading.Tasks;
using CodebaseMcpServer.Domain.SemanticSearch.ValueObjects;

namespace CodebaseMcpServer.Domain.SemanticSearch.Services
{
    public interface IEmbeddingService
    {
        Task<EmbeddingVector> GenerateEmbeddingAsync(CodeSnippet snippet);
        Task<List<EmbeddingVector>> GenerateEmbeddingsAsync(IEnumerable<CodeSnippet> snippets);
        int GetMaxTokens();
        EmbeddingProvider Provider { get; }
    }
}