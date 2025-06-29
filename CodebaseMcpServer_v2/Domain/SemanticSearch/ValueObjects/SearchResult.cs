using System.Collections.Generic;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects; // For FilePath
using CodebaseMcpServer.Domain.IndexManagement.Enums; // For ProjectType

namespace CodebaseMcpServer.Domain.SemanticSearch.ValueObjects
{
    public record SearchResult(
        IReadOnlyList<SearchResultItem> Items,
        int TotalMatches,
        string? QueryId = null
    );

    public record SearchResultItem(
        FilePath FilePath,
        string Content,
        ProjectType Language, // Assuming ProjectType is used for language
        int StartLine,
        int EndLine,
        double Score
    );
}