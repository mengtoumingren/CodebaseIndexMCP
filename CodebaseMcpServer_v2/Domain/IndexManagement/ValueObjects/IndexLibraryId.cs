using CodebaseMcpServer.Domain.Shared;
using System;

namespace CodebaseMcpServer.Domain.IndexManagement.ValueObjects
{
    public record IndexLibraryId
    {
        public int Value { get; init; }

        public IndexLibraryId(int value)
        {
            if (value <= 0)
                throw new ArgumentException("Library ID must be positive", nameof(value));
            Value = value;
        }
        
        public static implicit operator int(IndexLibraryId id) => id.Value;
        public static implicit operator IndexLibraryId(int value) => new(value);
    }
}