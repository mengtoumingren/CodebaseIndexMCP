using System;
using System.Collections.Generic;
using CodebaseMcpServer.Domain.Shared;

namespace CodebaseMcpServer.Domain.IndexManagement.ValueObjects
{
    public class IndexStatistics : ValueObject
    {
        public int TotalFiles { get; }
        public int IndexedFiles { get; }
        public long TotalSize { get; }
        public DateTime LastUpdated { get; }
        
        public IndexStatistics(int totalFiles, int indexedFiles, long totalSize, DateTime lastUpdated)
        {
            if (totalFiles < 0) throw new ArgumentException("Total files cannot be negative");
            if (indexedFiles < 0) throw new ArgumentException("Indexed files cannot be negative");
            if (indexedFiles > totalFiles) throw new ArgumentException("Indexed files cannot exceed total files");
            if (totalSize < 0) throw new ArgumentException("Total size cannot be negative");
            
            TotalFiles = totalFiles;
            IndexedFiles = indexedFiles;
            TotalSize = totalSize;
            LastUpdated = lastUpdated;
        }
        
        public static IndexStatistics Empty => new(0, 0, 0, DateTime.UtcNow);
        
        public IndexStatistics UpdateFromResult(IndexResult result)
        {
            return new IndexStatistics(
                result.TotalFiles,
                result.SuccessfulFiles,
                result.TotalSize,
                DateTime.UtcNow
            );
        }
        
        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return TotalFiles;
            yield return IndexedFiles;
            yield return TotalSize;
            yield return LastUpdated;
        }
    }
}