using CodebaseMcpServer.Application.Common.Interfaces;
using CodebaseMcpServer.Domain.IndexManagement.Enums;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;
using CodebaseMcpServer.Infrastructure.CodeAnalysis.Models;
using CodebaseMcpServer.Infrastructure.CodeAnalysis.Parsers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace CodebaseMcpServer.Infrastructure.CodeAnalysis.Parsers.CSharp
{
    public class CSharpCodeParser : ICodeParser
    {
        private readonly IFileSystemService _fileSystemService;
        private readonly ILogger<CSharpCodeParser> _logger;

        public CSharpCodeParser(IFileSystemService fileSystemService, ILogger<CSharpCodeParser> logger)
        {
            _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<CodeParseResult> ParseCodebaseAsync(
            CodebasePath codebasePath,
            IReadOnlyList<string> filePatterns,
            IReadOnlyList<string> excludePatterns)
        {
            var snippets = new List<CodeSnippet>();
            var errors = new List<string>();
            int totalFilesProcessed = 0;

            _logger.LogInformation("Starting C# codebase parsing for path: {CodebasePath}", codebasePath.Value);

            var files = _fileSystemService.EnumerateFiles(
                codebasePath.Value,
                "*",
                SearchOption.AllDirectories)
                .Where(f => filePatterns.Any(pattern => Path.GetFileName(f).Contains(pattern.Replace("*", "")))) // Simplified glob matching
                .Where(f => !excludePatterns.Any(pattern => f.Contains(pattern)));

            foreach (var filePath in files)
            {
                totalFilesProcessed++;
                try
                {
                    var fileContent = await _fileSystemService.ReadAllTextAsync(filePath);
                    var syntaxTree = CSharpSyntaxTree.ParseText(fileContent);
                    var root = await syntaxTree.GetRootAsync();

                    var methodDeclarations = root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>();
                    foreach (var method in methodDeclarations)
                    {
                        snippets.Add(new CodeSnippet(
                            new FilePath(Path.GetRelativePath(codebasePath.Value, filePath)),
                            method.ToString(),
                            ProjectType.CSharp, // Assuming CSharp for now
                            method.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                            method.GetLocation().GetLineSpan().EndLinePosition.Line + 1
                        ));
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error parsing file {filePath}: {ex.Message}");
                    _logger.LogError(ex, "Error parsing file: {FilePath}", filePath);
                }
            }

            _logger.LogInformation("Finished C# codebase parsing. Processed {TotalFiles} files, extracted {TotalSnippets} snippets.",
                totalFilesProcessed, snippets.Count);

            return new CodeParseResult(snippets, totalFilesProcessed, snippets.Count, errors);
        }
    }
}