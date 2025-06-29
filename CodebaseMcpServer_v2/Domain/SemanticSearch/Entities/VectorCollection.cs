using System;
using CodebaseMcpServer.Domain.Shared;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects; // For CollectionName
using CodebaseMcpServer.Domain.SemanticSearch.ValueObjects; // For EmbeddingProvider

namespace CodebaseMcpServer.Domain.SemanticSearch.Entities
{
    public class VectorCollection : Entity<CollectionName>
    {
        public CollectionName Name { get; private set; } = default!;
        public EmbeddingProvider Provider { get; private set; }
        public int Dimensions { get; private set; }
        public CollectionStatus Status { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime LastUpdatedAt { get; private set; }
        public int DocumentCount { get; private set; }
        
        private VectorCollection() { } // Properties initialized above
        
        public static VectorCollection Create(
            CollectionName name,
            EmbeddingProvider provider,
            int dimensions)
        {
            return new VectorCollection
            {
                Id = name, // CollectionName is the ID for VectorCollection
                Name = name,
                Provider = provider,
                Dimensions = dimensions,
                Status = CollectionStatus.Creating,
                CreatedAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow,
                DocumentCount = 0
            };
        }
        
        public void MarkAsReady()
        {
            if (Status != CollectionStatus.Creating)
                throw new InvalidOperationException("Collection is not in creating state");
                
            Status = CollectionStatus.Ready;
            LastUpdatedAt = DateTime.UtcNow;
        }
        
        public void UpdateDocumentCount(int count)
        {
            DocumentCount = count;
            LastUpdatedAt = DateTime.UtcNow;
        }
    }
}