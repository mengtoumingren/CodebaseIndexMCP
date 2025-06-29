using System.Collections.Generic;
using System.Threading.Tasks;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects; // For CodebasePath
using CodebaseMcpServer.Infrastructure.CodeAnalysis.Models; // For CodeParseResult

namespace CodebaseMcpServer.Infrastructure.CodeAnalysis.Parsers
{
    public interface ICodeParser
    {
        Task<CodeParseResult> ParseCodebaseAsync(
            CodebasePath codebasePath,
            IReadOnlyList<string> filePatterns,
            IReadOnlyList<string> excludePatterns);
    }
}