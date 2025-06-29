using System;

namespace CodebaseMcpServer.Application.IndexManagement.DTOs
{
    public record IndexStatisticsDto(
        int TotalFiles,
        int IndexedFiles,
        long TotalSize,
        DateTime LastUpdated,
        double CompletionPercentage
    );
}