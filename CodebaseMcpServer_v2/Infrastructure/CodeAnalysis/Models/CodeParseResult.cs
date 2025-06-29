using System.Collections.Generic;
using CodebaseMcpServer.Domain.SemanticSearch.ValueObjects; // For CodeSnippet

namespace CodebaseMcpServer.Infrastructure.CodeAnalysis.Models
{
    public record CodeParseResult(
        IReadOnlyList<CodeSnippet> Snippets,
        int TotalFilesProcessed,
        int TotalSnippetsExtracted,
        IReadOnlyList<string> Errors
    );
}