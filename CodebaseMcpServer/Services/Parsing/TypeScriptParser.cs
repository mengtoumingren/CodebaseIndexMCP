using CodebaseMcpServer.Models;
using System.Text.RegularExpressions;

namespace CodebaseMcpServer.Services.Parsing;

/// <summary>
/// TypeScript and TSX parser using regular expressions to extract key structures.
/// </summary>
public class TypeScriptParser : ICodeParser
{
    public string Language => "typescript";
    public string DisplayName => "TypeScript (Regex)";
    public IEnumerable<string> SupportedExtensions => new[] { ".ts", ".tsx" };

    // Regex to find top-level functions, classes, interfaces, enums, and type aliases.
    private static readonly Regex SnippetStartRegex = new(
        @"^" + // Start of a line
        @"(?:" +
        // Matches: export default function | export function | async function | function
        @"(?:export\s+(?:default\s+)?)?(?:async\s+)?function\s+(?<name>[\w$]+)\s*<?.*>?\(.*?\)\s*\{" +
        @"|" +
        // Matches: export default class | export class | class
        @"(?:export\s+(?:default\s+)?)?class\s+(?<name>[\w$]+)(?:\s+implements\s+[\w, ]+)?(?:\s+extends\s+[\w$.]+)?\s*\{" +
        @"|" +
        // Matches: export interface | interface
        @"(?:export\s+)?interface\s+(?<name>[\w$]+)(?:\s+extends\s+[\w, ]+)?\s*\{" +
        @"|" +
        // Matches: export enum | enum
        @"(?:export\s+)?enum\s+(?<name>[\w$]+)\s*\{" +
        @"|" +
        // Matches: export type | type
        @"(?:export\s+)?type\s+(?<name>[\w$]+)\s*=\s*.*(?:\{|;)" +
        @"|" +
        // Matches: export const | const | let | var (for arrow functions)
        @"(?:export\s+)?(?:const|let|var)\s+(?<name>[\w$]+)(?::\s*[\w<>\[\]\s]+)?\s*=\s*(?:async\s*)?\(.*?\)\s*=>\s*\{" +
        @")",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    public bool SupportsFile(string filePath)
    {
        var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
        return extension is ".ts" or ".tsx";
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
            Console.WriteLine($"[ERROR] Failed to parse TS file: {filePath}, Error: {ex.Message}");
            return new List<CodeSnippet>();
        }
    }

    public List<CodeSnippet> ParseCodeContent(string filePath, string content)
    {
        var snippets = new List<CodeSnippet>();
        if (string.IsNullOrWhiteSpace(content))
            return snippets;

        var matches = SnippetStartRegex.Matches(content);

        foreach (Match match in matches)
        {
            var name = match.Groups["name"].Value;
            var memberType = GetMemberType(match.Value);
            
            // For types ending with ';', the match is the whole snippet
            if (match.Value.Trim().EndsWith(';'))
            {
                 snippets.Add(new CodeSnippet
                {
                    FilePath = filePath,
                    ClassName = Path.GetFileNameWithoutExtension(filePath),
                    MethodName = $"{name} ({memberType})",
                    Code = match.Value,
                    StartLine = GetLineNumber(content, match.Index),
                    EndLine = GetLineNumber(content, match.Index + match.Length)
                });
                continue;
            }

            int bodyStartIndex = match.Index + match.Length - 1; // The opening brace
            int bodyEndIndex = FindMatchingBrace(content, bodyStartIndex);

            if (bodyEndIndex == -1) continue; // No matching brace found

            var code = content.Substring(match.Index, bodyEndIndex - match.Index + 1);

            snippets.Add(new CodeSnippet
            {
                FilePath = filePath,
                Namespace = null, // TS/JS doesn't have namespaces
                ClassName = Path.GetFileNameWithoutExtension(filePath), // Simplified, using filename
                MethodName = $"{name} ({memberType})",
                Code = code,
                StartLine = GetLineNumber(content, match.Index),
                EndLine = GetLineNumber(content, bodyEndIndex)
            });
        }

        // If no snippets were found, treat the whole file as a single snippet
        if (snippets.Count == 0)
        {
            snippets.Add(new CodeSnippet
            {
                FilePath = filePath,
                Namespace = null,
                ClassName = Path.GetFileNameWithoutExtension(filePath),
                MethodName = "Entire File",
                Code = content,
                StartLine = 1,
                EndLine = content.Split('\n').Length
            });
        }

        return snippets;
    }

    private static string GetMemberType(string matchValue)
    {
        if (matchValue.Contains("class")) return "Class";
        if (matchValue.Contains("interface")) return "Interface";
        if (matchValue.Contains("enum")) return "Enum";
        if (matchValue.Contains("type")) return "Type Alias";
        if (matchValue.Contains("=>")) return "Arrow Function";
        if (matchValue.Contains("function")) return "Function";
        return "Unknown";
    }

    private static int FindMatchingBrace(string text, int startIndex)
    {
        if (startIndex < 0 || startIndex >= text.Length || text[startIndex] != '{')
            return -1;

        int braceCount = 1;
        for (int i = startIndex + 1; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{')
            {
                braceCount++;
            }
            else if (c == '}')
            {
                braceCount--;
                if (braceCount == 0)
                {
                    return i;
                }
            }
        }
        return -1; // No matching brace found
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