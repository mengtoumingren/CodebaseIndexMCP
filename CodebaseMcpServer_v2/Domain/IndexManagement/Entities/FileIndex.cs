using System;
using CodebaseMcpServer.Domain.Shared;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;

namespace CodebaseMcpServer.Domain.IndexManagement.Entities
{
    public class FileIndex : Entity<int>
    {
        public FilePath FilePath { get; private set; } = default!;
        public IndexContent Content { get; private set; } = default!;
        public DateTime CreatedAt { get; private set; }
        public DateTime UpdatedAt { get; private set; }
        
        // EF Core constructor
        private FileIndex() { } // Properties initialized above
        
        public FileIndex(FilePath filePath, IndexContent content)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            CreatedAt = DateTime.UtcNow;
            UpdatedAt = DateTime.UtcNow;
        }
        
        public void UpdateContent(IndexContent newContent)
        {
            Content = newContent ?? throw new ArgumentNullException(nameof(newContent));
            UpdatedAt = DateTime.UtcNow;
        }
    }
}