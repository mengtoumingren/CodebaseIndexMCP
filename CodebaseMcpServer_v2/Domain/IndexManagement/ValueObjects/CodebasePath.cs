using System;
using System.IO;
using System.Collections.Generic;
using CodebaseMcpServer.Domain.Shared;

namespace CodebaseMcpServer.Domain.IndexManagement.ValueObjects
{
    public class CodebasePath : ValueObject
    {
        public string Value { get; }
        
        public CodebasePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Codebase path cannot be empty", nameof(value));
            if (!Directory.Exists(value))
                throw new ArgumentException($"Directory does not exist: {value}", nameof(value));
                
            Value = Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar);
        }
        
        public string NormalizedPath => Value;
        
        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }
        
        public static implicit operator string(CodebasePath path) => path.Value;
        public static implicit operator CodebasePath(string value) => new(value);
    }
}