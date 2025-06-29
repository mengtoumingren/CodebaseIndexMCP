using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Domain.SemanticSearch.ValueObjects;
using CodebaseMcpServer.Domain.SemanticSearch.Entities;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects; // For CollectionName

namespace CodebaseMcpServer.Infrastructure.SemanticSearch
{
    public interface IVectorDatabase
    {
        Task<bool> CreateCollectionAsync(VectorCollection collection, CancellationToken cancellationToken = default);
        Task<bool> DeleteCollectionAsync(CollectionName collectionName, CancellationToken cancellationToken = default);
        Task<bool> CollectionExistsAsync(CollectionName collectionName, CancellationToken cancellationToken = default);
        
        Task<string> UpsertVectorAsync(
            CollectionName collectionName,
            string documentId,
            EmbeddingVector vector,
            Dictionary<string, object> metadata,
            CancellationToken cancellationToken = default);
            
        Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            CollectionName collectionName,
            EmbeddingVector queryVector,
            int limit = 10,
            double threshold = 0.7,
            CancellationToken cancellationToken = default);
            
        Task<bool> DeleteVectorAsync(
            CollectionName collectionName,
            string documentId,
            CancellationToken cancellationToken = default);
            
        Task<VectorCollectionInfo> GetCollectionInfoAsync(
            CollectionName collectionName,
            CancellationToken cancellationToken = default);
    }
    
    public record VectorSearchResult(
        string DocumentId,
        double Score,
        Dictionary<string, object> Metadata);
        
    public record VectorCollectionInfo(
        CollectionName Name,
        int DocumentCount,
        int VectorDimensions,
        CollectionStatus Status);
}