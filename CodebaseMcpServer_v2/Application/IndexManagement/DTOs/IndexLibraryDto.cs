using System;

namespace CodebaseMcpServer.Application.IndexManagement.DTOs
{
    public record IndexLibraryDto(
        int Id,
        string CodebasePath,
        string CollectionName,
        string Status,
        int TotalFiles,
        int IndexedFiles,
        long TotalSize,
        DateTime CreatedAt,
        DateTime? LastIndexedAt,
        bool IsWatchingEnabled
    );
}