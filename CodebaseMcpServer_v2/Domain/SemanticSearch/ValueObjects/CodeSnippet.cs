using System;
using System.IO;
using System.Collections.Generic;
using CodebaseMcpServer.Domain.Shared;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects; // For FilePath

namespace CodebaseMcpServer.Domain.SemanticSearch.ValueObjects
{
    public class CodeSnippet : ValueObject
    {
        public FilePath FilePath { get; }
        public string Content { get; }
        public CodeLanguage Language { get; }
        public int StartLine { get; }
        public int EndLine { get; }

        public CodeSnippet(
            FilePath filePath,
            string content,
            CodeLanguage language,
            int startLine,
            int endLine)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Language = language;
            StartLine = startLine;
            EndLine = endLine;
        }

        public int TokenCount => EstimateTokenCount(Content);
        
        public CodeSnippet TruncateToTokenLimit(int maxTokens)
        {
            if (TokenCount <= maxTokens) return this;
            
            var truncatedContent = TruncateContent(Content, maxTokens);
            return new CodeSnippet(FilePath, truncatedContent, Language, StartLine, EndLine);
        }
        
        private static int EstimateTokenCount(string content)
        {
            // 简化的Token估算，实际可能需要更精确的实现
            return (int)Math.Ceiling(content.Length / 4.0);
        }
        
        private static string TruncateContent(string content, int maxTokens)
        {
            var maxLength = maxTokens * 4; // 简化估算
            return content.Length <= maxLength ? content : content[..maxLength];
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return FilePath;
            yield return Content;
            yield return Language;
            yield return StartLine;
            yield return EndLine;
        }
    }

    public enum CodeLanguage
    {
        Unknown,
        CSharp,
        Python,
        JavaScript,
        TypeScript,
        Java,
        Go,
        Rust,
        Cpp,
        C,
        Html,
        Css,
        Json,
        Xml,
        Yaml,
        Markdown,
        Text
    }
}