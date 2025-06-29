using System;
using System.Collections.Generic;
using CodebaseMcpServer.Domain.Shared;

namespace CodebaseMcpServer.Domain.IndexManagement.ValueObjects
{
    public class IndexContent : ValueObject
    {
        public string Content { get; }
        public string Language { get; }
        public long Size { get; }
        public DateTime LastModified { get; }
        public string Hash { get; }
        
        public IndexContent(
            string content,
            string language,
            long size,
            DateTime lastModified,
            string hash)
        {
            if (string.IsNullOrEmpty(content))
                throw new ArgumentException("Content cannot be null or empty");
            if (size < 0)
                throw new ArgumentException("Size cannot be negative");
            if (string.IsNullOrWhiteSpace(hash))
                throw new ArgumentException("Hash cannot be null or empty");
                
            Content = content;
            Language = language ?? "unknown";
            Size = size;
            LastModified = lastModified;
            Hash = hash;
        }
        
        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Content;
            yield return Language;
            yield return Size;
            yield return LastModified;
            yield return Hash;
        }
    }
}