using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using CodeSearch;

namespace CodebaseMcpServer.Tools;

/// <summary>
/// 代码搜索 MCP 工具
/// </summary>
[McpServerToolType]
public sealed class CodeSearchTools
{
    private static CodeSemanticSearch? _searchSystem;
    
    /// <summary>
    /// 初始化搜索系统
    /// </summary>
    private static CodeSemanticSearch GetSearchSystem()
    {
        if (_searchSystem == null)
        {
            Console.WriteLine("[DEBUG] 初始化 CodeSemanticSearch 系统...");
            _searchSystem = new CodeSemanticSearch(
                apiKey: "sk-a239bd73d5b947ed955d03d437ca1e70",
                collectionName: "csharp_code");
            Console.WriteLine("[DEBUG] CodeSemanticSearch 系统初始化完成");
        }
        return _searchSystem;
    }

    /// <summary>
    /// 语义代码搜索工具
    /// </summary>
    /// <param name="query">自然语言搜索查询</param>
    /// <param name="codebasePath">要搜索的代码库路径（可选，如果不提供则需要确保代码库已被索引）</param>
    /// <param name="limit">返回结果数量限制（可选，默认10）</param>
    /// <returns>格式化的搜索结果</returns>
    [McpServerTool, Description("根据自然语言描述搜索相关代码片段，返回匹配的方法、类和代码块。当需要查看项目中特定功能或逻辑的代码实现时，可以使用此工具进行语义搜索。")]
    public static async Task<string> SemanticCodeSearch(
        [Description("自然语言搜索查询，例如：'身份认证逻辑'、'数据库连接'、'文件上传处理'、'异常处理机制'、'配置管理'")] string query,
        [Description("要搜索的代码库路径。如果不提供则默认使用当前工作目录。必须提供完整的绝对路径，例如：'C:\\Projects\\MyApp' 或 '/home/user/projects/myapp'")] string? codebasePath = null,
        [Description("返回结果数量限制，默认为10个结果")] int limit = 10)
    {
        try
        {
            Console.WriteLine($"[INFO] 开始执行语义搜索，查询: '{query}'");
            
            // 获取搜索系统实例
            var searchSystem = GetSearchSystem();
            
            // // 如果提供了代码库路径，先处理代码库
            // if (!string.IsNullOrEmpty(codebasePath))
            // {
            //     Console.WriteLine($"[INFO] 开始处理代码库: {codebasePath}");
            //     var indexedCount = await searchSystem.ProcessCodebase(codebasePath);
            //     Console.WriteLine($"[INFO] 已索引 {indexedCount} 个C#代码片段");
            // }
            
            // 执行搜索
            Console.WriteLine($"[DEBUG] 开始搜索: {query}");
            var results = await searchSystem.Search(query, limit: limit);
            
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
                
                
                resultBuilder.AppendLine("```csharp");
                resultBuilder.AppendLine(snippet.Code);
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
            Console.WriteLine($"[ERROR] 异常类型: {ex.GetType().Name}");
            Console.WriteLine($"[ERROR] 堆栈跟踪: {ex.StackTrace}");
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[ERROR] 内部异常: {ex.InnerException.GetType().Name}");
                Console.WriteLine($"[ERROR] 内部异常消息: {ex.InnerException.Message}");
            }
            
            return $"搜索过程中发生错误: {ex.Message}\n\n请检查：\n1. 代码库路径是否正确\n2. Qdrant 服务是否正常运行\n3. API 配置是否正确";
        }
    }
}