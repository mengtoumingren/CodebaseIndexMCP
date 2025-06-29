using System;
using System.Collections.Generic;
using System.IO;
using CodebaseMcpServer.Domain.Shared;

namespace CodebaseMcpServer.Domain.IndexManagement.ValueObjects
{
    public class FilePath : ValueObject
    {
        public string RelativePath { get; }
        
        public FilePath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new ArgumentException("File path cannot be empty", nameof(relativePath));
            if (Path.IsPathRooted(relativePath))
                throw new ArgumentException("File path must be relative", nameof(relativePath));
                
            RelativePath = relativePath.Replace('\\', '/');
        }
        
        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return RelativePath;
        }
        
        public static implicit operator string(FilePath path) => path.RelativePath;
        public static implicit operator FilePath(string value) => new(value);
    }
}