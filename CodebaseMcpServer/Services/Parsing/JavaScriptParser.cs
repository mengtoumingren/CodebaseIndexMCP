using CodebaseMcpServer.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace CodebaseMcpServer.Services.Parsing;

/// <summary>
/// 增强的 JavaScript 解析器，能够提取顶层的类和函数作为代码片段。
/// </summary>
public class JavaScriptParser : ICodeParser
{
    public string Language => "javascript";
    public string DisplayName => "JavaScript (Enhanced)";
    public IEnumerable<string> SupportedExtensions => new[] { ".js", ".jsx", ".mjs", ".cjs" };

    // 正则表达式，用于匹配顶层的函数、类和赋值给变量的箭头函数
    // 使用 Multiline 模式，`^` 匹配每行的开头
    private static readonly Regex SnippetStartRegex = new(
        @"^" + // 匹配行首
        @"(?:" +
        // 匹配: export default function | export function | async function | function
        @"(?:export\s+(?:default\s+)?)?(?:async\s+)?function\s+(?<name>[\w$]+)\s*\(.*?\)\s*\{" +
        @"|" +
        // 匹配: export default class | export class | class
        @"(?:export\s+(?:default\s+)?)?class\s+(?<name>[\w$]+)(?:\s+extends\s+[\w$.]+)?\s*\{" +
        @"|" +
        // 匹配: export const | const | let | var
        @"(?:export\s+)?(?:const|let|var)\s+(?<name>[\w$]+)\s*=\s*(?:async\s*)?\(.*?\)\s*=>\s*\{" +
        @")",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    public bool SupportsFile(string filePath)
    {
        var extension = Path.GetExtension(filePath)?.ToLowerInvariant();
        return extension switch
        {
            ".js" => true,
            ".jsx" => true,
            ".mjs" => true,
            ".cjs" => true,
            _ => false
        };
    }

    public List<CodeSnippet> ParseCodeFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[WARNING] 文件不存在: {filePath}");
            return new List<CodeSnippet>();
        }

        try
        {
            var content = File.ReadAllText(filePath);
            return ParseCodeContent(filePath, content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 解析JS文件失败: {filePath}, 错误: {ex.Message}");
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
            int bodyStartIndex = match.Index + match.Length - 1; // The opening brace
            int bodyEndIndex = FindMatchingBrace(content, bodyStartIndex);

            if (bodyEndIndex == -1) continue; // 没有找到匹配的括号

            var code = content.Substring(match.Index, bodyEndIndex - match.Index + 1);
            var name = match.Groups["name"].Value;
            var memberType = GetMemberType(match.Value);

            snippets.Add(new CodeSnippet
            {
                FilePath = filePath,
                Namespace = null, // JS 没有命名空间
                ClassName = Path.GetFileNameWithoutExtension(filePath), // 简化处理，使用文件名
                MethodName = $"{name} ({memberType})",
                Code = code,
                StartLine = GetLineNumber(content, match.Index),
                EndLine = GetLineNumber(content, bodyEndIndex)
            });
        }

        // 如果没有找到任何片段，则将整个文件作为一个片段
        if (snippets.Count == 0)
        {
            snippets.Add(new CodeSnippet
            {
                FilePath = filePath,
                Namespace = null,
                ClassName = Path.GetFileNameWithoutExtension(filePath),
                MethodName = "整个文件",
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
        return -1; // 未找到匹配的括号
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