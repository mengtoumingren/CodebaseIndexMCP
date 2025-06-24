using CodebaseMcpServer.Models;
using System.Text.RegularExpressions;

namespace CodebaseMcpServer.Services.Parsing;

/// <summary>
/// A simple parser for .cshtml (Razor) files to extract C# code blocks.
/// This parser focuses on extracting code from @code blocks, @functions blocks, and simple @{ ... } blocks.
/// </summary>
public class CshtmlParser : ICodeParser
{
    public string Language => "cshtml";
    public string DisplayName => "CSHTML (Razor)";
    public IEnumerable<string> SupportedExtensions => new[] { ".cshtml" };

    // Regex to find @code{...}, @functions{...}, and @{...} blocks.
    private static readonly Regex CodeBlockRegex = new(
        @"@(code|functions|)\s*\{(?<code>.*?)\}",
        RegexOptions.Compiled | RegexOptions.Singleline
    );

    public bool SupportsFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".cshtml", StringComparison.OrdinalIgnoreCase);
    }

    public List<CodeSnippet> ParseCodeFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[WARNING] File does not exist: {filePath}");
            return new List<CodeSnippet>();
        }

        try
        {
            var content = File.ReadAllText(filePath);
            return ParseCodeContent(filePath, content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Failed to parse CSHTML file: {filePath}, Error: {ex.Message}");
            return new List<CodeSnippet>();
        }
    }

    public List<CodeSnippet> ParseCodeContent(string filePath, string content)
    {
        var snippets = new List<CodeSnippet>();
        if (string.IsNullOrWhiteSpace(content))
            return snippets;

        var matches = CodeBlockRegex.Matches(content);

        int blockNumber = 1;
        foreach (Match match in matches)
        {
            var code = match.Groups["code"].Value.Trim();
            if (string.IsNullOrWhiteSpace(code)) continue;

            var memberType = match.Groups[1].Value switch
            {
                "code" => "Code Block",
                "functions" => "Functions Block",
                _ => "Razor Block"
            };
            
            var snippet = new CodeSnippet
            {
                FilePath = filePath,
                Namespace = null, // CSHTML files don't have a formal namespace in this context
                ClassName = Path.GetFileNameWithoutExtension(filePath), // Use filename as a class identifier
                MethodName = $"Block {blockNumber} ({memberType})",
                Code = code,
                StartLine = GetLineNumber(content, match.Index),
                EndLine = GetLineNumber(content, match.Index + match.Length)
            };
            snippets.Add(snippet);
            blockNumber++;
        }

        // If no specific code blocks are found, treat the whole file as a single snippet
        // to ensure it's still indexed for its HTML/Razor content.
        if (snippets.Count == 0)
        {
            snippets.Add(new CodeSnippet
            {
                FilePath = filePath,
                ClassName = Path.GetFileNameWithoutExtension(filePath),
                MethodName = "Entire File",
                Code = content,
                StartLine = 1,
                EndLine = content.Split('\n').Length
            });
        }

        return snippets;
    }

    private static int GetLineNumber(string text, int position)
    {
        int lineNumber = 1;
        for (int i = 0; i < position && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lineNumber++;
            }
        }
        return lineNumber;
    }
}