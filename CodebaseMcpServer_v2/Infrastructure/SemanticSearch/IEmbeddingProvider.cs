using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Domain.SemanticSearch.ValueObjects;

namespace CodebaseMcpServer.Infrastructure.SemanticSearch
{
    public interface IEmbeddingProvider
    {
        EmbeddingProvider ProviderType { get; }
        int MaxTokens { get; }
        int Dimensions { get; }
        
        Task<EmbeddingVector> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<EmbeddingVector>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);
        Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
    }
}