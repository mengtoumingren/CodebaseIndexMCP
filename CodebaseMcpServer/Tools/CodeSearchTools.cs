using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using CodebaseMcpServer.Services;

namespace CodebaseMcpServer.Tools;

/// <summary>
/// 代码搜索 MCP 工具
/// </summary>
[McpServerToolType]
public sealed class CodeSearchTools
{
    /// <summary>
    /// 语义代码搜索工具
    /// </summary>
    /// <param name="codeSearchService">代码搜索服务</param>
    /// <param name="query">自然语言搜索查询</param>
    /// <param name="codebasePath">要搜索的代码库路径（可选，默认使用配置文件中的路径）</param>
    /// <param name="limit">返回结果数量限制（可选，默认10）</param>
    /// <returns>格式化的搜索结果</returns>
    [McpServerTool, Description("根据自然语言描述搜索相关代码片段，返回匹配的方法、类和代码块")]
    public static async Task<string> SemanticCodeSearch(
        ICodeSearchService codeSearchService,
        [Description("自然语言搜索查询，例如：'身份认证逻辑'、'数据库连接'、'文件上传处理'")] string query,
        [Description("要搜索的代码库路径，如果不提供则使用默认配置路径")] string? codebasePath = null,
        [Description("返回结果数量限制")] int limit = 10)
    {
        try
        {
            Console.WriteLine($"[INFO] 开始执行语义搜索，查询: '{query}'");
            
            // 执行搜索
            var results = await codeSearchService.SearchAsync(query, codebasePath, limit);
            
            if (!results.Any())
            {
                return $"未找到与查询 '{query}' 相关的代码片段。\n\n建议：\n1. 尝试使用不同的关键词\n2. 检查代码库路径是否正确\n3. 确认代码库是否包含相关代码";
            }

            // 格式化搜索结果
            var resultBuilder = new StringBuilder();
            resultBuilder.AppendLine($"找到 {results.Count} 个相关代码片段:\n");

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                var snippet = result.Snippet;

                resultBuilder.AppendLine($"--- 结果 {i + 1} (相似度得分: {result.Score:F4}) ---");
                resultBuilder.AppendLine($"文件: {snippet.FilePath}");
                
                if (!string.IsNullOrEmpty(snippet.Namespace))
                    resultBuilder.AppendLine($"命名空间: {snippet.Namespace}");
                
                if (!string.IsNullOrEmpty(snippet.ClassName))
                    resultBuilder.AppendLine($"类: {snippet.ClassName}");
                
                if (!string.IsNullOrEmpty(snippet.MethodName))
                    resultBuilder.AppendLine($"成员: {snippet.MethodName}");

                resultBuilder.AppendLine($"位置: 第 {snippet.StartLine}-{snippet.EndLine} 行");
                
                // 代码预览 - 限制长度以避免输出过长
                var codePreview = snippet.Code.Length > 300 
                    ? snippet.Code.Substring(0, 300) + "..."
                    : snippet.Code;
                
                resultBuilder.AppendLine($"代码预览:");
                resultBuilder.AppendLine("```csharp");
                resultBuilder.AppendLine(codePreview);
                resultBuilder.AppendLine("```");
                
                if (i < results.Count - 1)
                    resultBuilder.AppendLine(); // 添加空行分隔
            }

            Console.WriteLine($"[INFO] 搜索完成，返回 {results.Count} 个结果");
            return resultBuilder.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 搜索过程中发生错误: {ex.Message}");
            return $"搜索过程中发生错误: {ex.Message}\n\n请检查：\n1. 代码库路径是否正确\n2. Qdrant 服务是否正常运行\n3. API 配置是否正确";
        }
    }
}