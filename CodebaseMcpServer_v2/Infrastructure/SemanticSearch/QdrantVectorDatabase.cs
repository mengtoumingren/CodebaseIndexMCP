using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Domain.SemanticSearch.ValueObjects;
using CodebaseMcpServer.Domain.SemanticSearch.Entities;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects; // For CollectionName
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using CollectionStatus = CodebaseMcpServer.Domain.SemanticSearch.Entities.CollectionStatus;

namespace CodebaseMcpServer.Infrastructure.SemanticSearch
{
    public class QdrantVectorDatabase : IVectorDatabase
    {
        private readonly QdrantClient _client;
        private readonly QdrantOptions _options;
        private readonly ILogger<QdrantVectorDatabase> _logger;
        
        public QdrantVectorDatabase(
            QdrantClient client,
            IOptions<QdrantOptions> options,
            ILogger<QdrantVectorDatabase> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<bool> CreateCollectionAsync(VectorCollection collection, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Creating collection: {CollectionName}", collection.Name.Value);
                
                var createRequest = new CreateCollection
                {
                    CollectionName = collection.Name.Value,
                    VectorsConfig = new VectorsConfig
                    {
                        Params = new VectorParams
                        {
                            Size = (ulong)collection.Dimensions,
                            Distance = Distance.Cosine
                        }
                    }
                };
                
                await _client.CreateCollectionAsync(createRequest.CollectionName, createRequest.VectorsConfig.Params, cancellationToken: cancellationToken);
                
                _logger.LogInformation("Successfully created collection: {CollectionName}", collection.Name.Value);
                return true; // Assuming success if no exception
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create collection: {CollectionName}", collection.Name.Value);
                return false;
            }
        }
        
        public async Task<bool> DeleteCollectionAsync(CollectionName collectionName, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Deleting collection: {CollectionName}", collectionName.Value);
                
                await _client.DeleteCollectionAsync(collectionName.Value, cancellationToken: cancellationToken);
                
                _logger.LogInformation("Successfully deleted collection: {CollectionName}", collectionName.Value);
                return true; // Assuming success if no exception
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete collection: {CollectionName}", collectionName.Value);
                return false;
            }
        }
        
        public async Task<bool> CollectionExistsAsync(CollectionName collectionName, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _client.GetCollectionInfoAsync(collectionName.Value, cancellationToken: cancellationToken);
                return response != null;
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking collection existence: {CollectionName}", collectionName.Value);
                throw;
            }
        }
        
        public async Task<string> UpsertVectorAsync(
            CollectionName collectionName,
            string documentId,
            EmbeddingVector vector,
            Dictionary<string, object> metadata,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Upserting vector for document: {DocumentId} in collection: {CollectionName}", 
                    documentId, collectionName.Value);
                
                var point = new PointStruct
                {
                    Id = new PointId { Uuid = documentId },
                    Vectors = new Qdrant.Client.Grpc.Vectors
                    {
                        Vector = new Qdrant.Client.Grpc.Vector()
                    },
                };
                point.Vectors.Vector.Data.AddRange(vector.Values.ToArray());

                foreach (var kvp in metadata)
                {
                    point.Payload[kvp.Key] = new Value { StringValue = kvp.Value?.ToString() ?? "" };
                }
                
                var response = await _client.UpsertAsync(collectionName.Value, new List<PointStruct> { point }, wait: true, cancellationToken: cancellationToken);
                
                _logger.LogDebug("Successfully upserted vector for document: {DocumentId}", documentId);
                return documentId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upsert vector for document: {DocumentId}", documentId);
                throw;
            }
        }
        
        public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
            CollectionName collectionName,
            EmbeddingVector queryVector,
            int limit = 10,
            double threshold = 0.7,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Searching in collection: {CollectionName} with limit: {Limit}", 
                    collectionName.Value, limit);
                
                var response = await _client.SearchAsync(
                    collectionName.Value,
                    queryVector.Values.AsMemory(),
                    limit: (ulong)limit,
                    scoreThreshold: (float)threshold,
                    cancellationToken: cancellationToken);
                
                var results = response.Select(point => new VectorSearchResult(
                    point.Id.Uuid,
                    point.Score,
                    point.Payload.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value.StringValue)
                )).ToList();
                
                _logger.LogDebug("Found {Count} results in collection: {CollectionName}", 
                    results.Count, collectionName.Value);
                    
                return results;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search in collection: {CollectionName}", collectionName.Value);
                throw;
            }
        }
        
        public async Task<bool> DeleteVectorAsync(
            CollectionName collectionName,
            string documentId,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Deleting vector for document: {DocumentId} from collection: {CollectionName}", 
                    documentId, collectionName.Value);
                
                var deleteRequest = new DeletePoints
                {
                    CollectionName = collectionName.Value,
                    Points = new PointsSelector
                    {
                        Points = new PointsIdsList
                        {
                            Ids = { new PointId { Uuid = documentId } }
                        }
                    }
                };
                
                var response = await _client.DeleteAsync(collectionName.Value, Guid.Parse(documentId), wait: true, cancellationToken: cancellationToken);
                
                _logger.LogDebug("Successfully deleted vector for document: {DocumentId}", documentId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete vector for document: {DocumentId}", documentId);
                return false;
            }
        }
        
        public async Task<VectorCollectionInfo> GetCollectionInfoAsync(
            CollectionName collectionName,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _client.GetCollectionInfoAsync(collectionName.Value, cancellationToken: cancellationToken);
                
                return new VectorCollectionInfo(
                    collectionName,
                    (int)response.PointsCount,
                    (int)response.Config.Params.VectorsConfig.Params.Size,
                    CollectionStatus.Ready
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get collection info: {CollectionName}", collectionName.Value);
                throw;
            }
        }

    }
    
    public class QdrantOptions
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 6334;
        public bool UseTls { get; set; } = false;
        public string? ApiKey { get; set; }
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}