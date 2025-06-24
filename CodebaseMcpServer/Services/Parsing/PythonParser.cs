using CodebaseMcpServer.Models;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace CodebaseMcpServer.Services.Parsing;

/// <summary>
/// A simple Python parser using regular expressions to extract class and function definitions.
/// </summary>
public class PythonParser : ICodeParser
{
    public string Language => "python";
    public string DisplayName => "Python (Regex)";
    public IEnumerable<string> SupportedExtensions => new[] { ".py" };

    // Regex to find top-level functions (def) and classes (class).
    // It captures the indentation to help determine the scope.
    private static readonly Regex SnippetStartRegex = new(
        @"^(?<indent>[\s]*)?" + // Optional leading whitespace (indentation)
        @"(?:(?<type>class|def)\s+(?<name>[\w_]+)\s*\(?.*?\)?\s*:)",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    public bool SupportsFile(string filePath)
    {
        return Path.GetExtension(filePath).Equals(".py", StringComparison.OrdinalIgnoreCase);
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
            Console.WriteLine($"[ERROR] Failed to parse Python file: {filePath}, Error: {ex.Message}");
            return new List<CodeSnippet>();
        }
    }

    public List<CodeSnippet> ParseCodeContent(string filePath, string content)
    {
        var snippets = new List<CodeSnippet>();
        if (string.IsNullOrWhiteSpace(content))
            return snippets;

        var matches = SnippetStartRegex.Matches(content);
        var lines = content.Split('\n');

        for (int i = 0; i < matches.Count; i++)
        {
            Match match = matches[i];
            var name = match.Groups["name"].Value;
            var type = match.Groups["type"].Value;
            var memberType = type == "class" ? "Class" : "Function";
            
            int startLine = GetLineNumber(content, match.Index);
            string baseIndent = match.Groups["indent"].Value ?? "";

            int endLine = startLine;
            for (int j = startLine; j < lines.Length; j++)
            {
                string currentLine = lines[j];
                if (string.IsNullOrWhiteSpace(currentLine))
                {
                    endLine = j + 1;
                    continue;
                }
                
                string lineIndent = GetIndent(currentLine);

                if (lineIndent.Length <= baseIndent.Length && !string.IsNullOrWhiteSpace(currentLine.Trim()))
                {
                    break;
                }
                endLine = j + 1;
            }
            
            // Find next match's start line to cap the current snippet's end line
            int nextMatchStartLine = (i + 1 < matches.Count) ? GetLineNumber(content, matches[i + 1].Index) : lines.Length + 1;
            
            int blockEndLine = startLine;
            for (int j = startLine; j < lines.Length; j++)
            {
                string currentLine = lines[j];
                string lineIndent = GetIndent(currentLine);

                if (j >= startLine && !string.IsNullOrWhiteSpace(currentLine) && lineIndent.Length <= baseIndent.Length)
                {
                    break;
                }
                blockEndLine = j + 1;
            }

            blockEndLine = Math.Min(blockEndLine, nextMatchStartLine -1);


            var codeLines = new List<string>();
            for(int k = startLine - 1; k < blockEndLine && k < lines.Length; k++)
            {
                codeLines.Add(lines[k]);
            }
            var code = string.Join("\n", codeLines);

            snippets.Add(new CodeSnippet
            {
                FilePath = filePath,
                ClassName = type == "class" ? name : Path.GetFileNameWithoutExtension(filePath),
                MethodName = $"{name} ({memberType})",
                Code = code,
                StartLine = startLine,
                EndLine = blockEndLine
            });
        }

        if (snippets.Count == 0)
        {
            snippets.Add(new CodeSnippet
            {
                FilePath = filePath,
                ClassName = Path.GetFileNameWithoutExtension(filePath),
                MethodName = "Entire File",
                Code = content,
                StartLine = 1,
                EndLine = lines.Length
            });
        }

        return snippets;
    }

    private static string GetIndent(string line)
    {
        var match = Regex.Match(line, @"^(\s*)");
        return match.Success ? match.Groups[1].Value : "";
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