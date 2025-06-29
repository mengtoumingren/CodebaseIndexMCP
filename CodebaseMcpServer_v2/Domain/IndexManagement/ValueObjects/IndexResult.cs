using System;
using System.Collections.Generic;
using CodebaseMcpServer.Domain.Shared;

namespace CodebaseMcpServer.Domain.IndexManagement.ValueObjects
{
    public class IndexResult : ValueObject
    {
        public int TotalFiles { get; }
        public int SuccessfulFiles { get; }
        public int FailedFiles { get; }
        public int ProcessedFiles { get; } // Added ProcessedFiles
        public long TotalSize { get; }
        public TimeSpan Duration { get; }
        public IReadOnlyList<string> Errors { get; }
        
        public IndexResult(
            int totalFiles,
            int successfulFiles,
            int failedFiles,
            int processedFiles, // Added ProcessedFiles
            long totalSize,
            TimeSpan duration,
            IReadOnlyList<string>? errors = null)
        {
            if (totalFiles < 0) throw new ArgumentException("Total files cannot be negative");
            if (successfulFiles < 0) throw new ArgumentException("Successful files cannot be negative");
            if (failedFiles < 0) throw new ArgumentException("Failed files cannot be negative");
            if (processedFiles < 0) throw new ArgumentException("Processed files cannot be negative"); // Added validation
            if (successfulFiles + failedFiles > totalFiles)
                throw new ArgumentException("Successful + Failed files cannot exceed total files");
            if (totalSize < 0) throw new ArgumentException("Total size cannot be negative");
            
            TotalFiles = totalFiles;
            SuccessfulFiles = successfulFiles;
            FailedFiles = failedFiles;
            ProcessedFiles = processedFiles; // Assigned ProcessedFiles
            TotalSize = totalSize;
            Duration = duration;
            Errors = errors ?? new List<string>();
        }
        
        public bool IsSuccessful => FailedFiles == 0;
        public double SuccessRate => TotalFiles == 0 ? 1.0 : (double)SuccessfulFiles / TotalFiles;
        
        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return TotalFiles;
            yield return SuccessfulFiles;
            yield return FailedFiles;
            yield return ProcessedFiles; // Added ProcessedFiles
            yield return TotalSize;
            yield return Duration;
            foreach (var error in Errors)
                yield return error;
        }
    }
}