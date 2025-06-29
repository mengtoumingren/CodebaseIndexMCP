using System;
using System.Collections.Generic;
using CodebaseMcpServer.Domain.Shared;

namespace CodebaseMcpServer.Domain.IndexManagement.ValueObjects
{
    public class CollectionName : ValueObject
    {
        public string Value { get; }
        
        public CollectionName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Collection name cannot be empty", nameof(value));
            if (value.Length > 100)
                throw new ArgumentException("Collection name too long", nameof(value));
                
            Value = value.Trim();
        }
        
        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Value;
        }
        
        public static implicit operator string(CollectionName name) => name.Value;
        public static implicit operator CollectionName(string value) => new(value);
    }
}