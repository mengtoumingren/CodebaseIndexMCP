using System;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects; // For FilePath
using CodebaseMcpServer.Domain.IndexManagement.Enums; // For ProjectType (CodeLanguage)

namespace CodebaseMcpServer.Infrastructure.CodeAnalysis.Models
{
    public record CodeSnippet(
        FilePath FilePath,
        string Content,
        ProjectType Language, // Using ProjectType as CodeLanguage
        int StartLine,
        int EndLine)
    {
        public int TokenCount => EstimateTokenCount(Content);
        
        public CodeSnippet TruncateToTokenLimit(int maxTokens)
        {
            if (TokenCount <= maxTokens) return this;
            
            var truncatedContent = TruncateContent(Content, maxTokens);
            return this with { Content = truncatedContent };
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
    }
}