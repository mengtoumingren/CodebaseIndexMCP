using System;
using System.Collections.Generic;
using System.Linq;
using CodebaseMcpServer.Domain.Shared;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;
using CodebaseMcpServer.Domain.IndexManagement.Enums;
using CodebaseMcpServer.Domain.IndexManagement.Events;
using CodebaseMcpServer.Domain.IndexManagement.Entities;

namespace CodebaseMcpServer.Domain.IndexManagement.Aggregates
{
    public class IndexLibrary : AggregateRoot<IndexLibraryId>
    {
        private readonly List<FileIndex> _fileIndexes = new();
        
        public CodebasePath CodebasePath { get; private set; } = default!;
        public CollectionName CollectionName { get; private set; } = default!;
        public IndexStatus Status { get; private set; }
        public WatchConfiguration? WatchConfig { get; private set; } = default!;
        public IndexStatistics Statistics { get; private set; } = default!;
        public DateTime CreatedAt { get; private set; }
        public DateTime? LastIndexedAt { get; private set; }
        
        // EF Core constructor
        private IndexLibrary() { }
        
        public IndexLibrary(
            IndexLibraryId id,
            CodebasePath codebasePath,
            CollectionName collectionName)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            CodebasePath = codebasePath ?? throw new ArgumentNullException(nameof(codebasePath));
            CollectionName = collectionName ?? throw new ArgumentNullException(nameof(collectionName));
            Status = IndexStatus.Pending;
            Statistics = IndexStatistics.Empty;
            CreatedAt = DateTime.UtcNow;
            
            AddDomainEvent(new IndexLibraryCreated(Id!, CodebasePath, CollectionName));
        }
        
        // 业务方法
        public void StartIndexing()
        {
            if (!CanStartIndexing())
                throw new InvalidOperationException($"Cannot start indexing in current state: {Status}");
                
            Status = IndexStatus.Indexing;
            AddDomainEvent(new IndexingStarted(Id!, CodebasePath, DateTime.UtcNow));
        }
        
        public void CompleteIndexing(IndexResult result)
        {
            if (Status != IndexStatus.Indexing)
                throw new InvalidOperationException($"Not currently indexing. Current status: {Status}");
                
            Status = IndexStatus.Completed;
            Statistics = Statistics.UpdateFromResult(result);
            LastIndexedAt = DateTime.UtcNow;
            AddDomainEvent(new IndexingCompleted(Id!, result));
        }
        
        public void FailIndexing(string errorMessage)
        {
            if (Status != IndexStatus.Indexing)
                throw new InvalidOperationException($"Not currently indexing. Current status: {Status}");
                
            Status = IndexStatus.Failed;
            AddDomainEvent(new IndexingFailed(Id!, errorMessage));
        }
        
        public void UpdateFileIndex(FilePath filePath, IndexContent content)
        {
            var existingIndex = _fileIndexes.FirstOrDefault(f => f.FilePath.Equals(filePath));
            if (existingIndex != null)
            {
                existingIndex.UpdateContent(content);
            }
            else
            {
                _fileIndexes.Add(new FileIndex(filePath, content));
            }
            
            AddDomainEvent(new FileIndexUpdated(Id!, filePath));
        }
        
        public void EnableWatching(WatchConfiguration config)
        {
            WatchConfig = config ?? throw new ArgumentNullException(nameof(config));
            AddDomainEvent(new WatchingEnabled(Id!, config));
        }
        
        public void DisableWatching()
        {
            if (WatchConfig == null)
                return;
                
            WatchConfig = null;
            AddDomainEvent(new WatchingDisabled(Id!));
        }
        
        public bool CanStartIndexing()
        {
            return Status == IndexStatus.Pending || Status == IndexStatus.Failed;
        }
        
        public IReadOnlyList<FileIndex> GetFileIndexes() => _fileIndexes.AsReadOnly();
    }
}