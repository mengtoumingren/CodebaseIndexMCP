using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using CodebaseMcpServer.Services;
using CodebaseMcpServer.Extensions;

namespace CodebaseMcpServer.Tools;

/// <summary>
/// 升级版代码搜索 MCP 工具 - 支持多集合搜索
/// </summary>
[McpServerToolType]
public sealed class CodeSearchTools
{
    private static EnhancedCodeSemanticSearch? _searchService;
    private static IndexConfigManager? _configManager;
    
    /// <summary>
    /// 初始化工具依赖
    /// </summary>
    public static void Initialize(EnhancedCodeSemanticSearch searchService, IndexConfigManager configManager)
    {
        _searchService = searchService;
        _configManager = configManager;
    }

    /// <summary>
    /// 语义代码搜索工具 - 升级版支持多代码库
    /// </summary>
    /// <param name="query">自然语言搜索查询</param>
    /// <param name="codebasePath">要搜索的代码库路径，从本地配置获取对应集合名称</param>
    /// <param name="limit">返回结果数量限制（可选，默认10）</param>
    /// <returns>格式化的搜索结果</returns>
    [McpServerTool, Description("直接在代码库中进行语义搜索，根据自然语言描述查找相关代码片段。如果代码库未建立索引，会提示是否创建索引库。")]
    public static async Task<string> SemanticCodeSearch(
        [Description("自然语言搜索查询，例如：'身份认证逻辑'、'数据库连接'、'文件上传处理'、'异常处理机制'、'配置管理'、'用户登录验证'、'数据加密'等")] string query,
        [Description("要搜索的代码库路径，通常是当前工作目录，例如：'d:/VSProject/MyApp' 或 'C:\\Projects\\MyProject'")] string codebasePath,
        [Description("返回结果数量限制，默认为10个结果")] int limit = 10)
    {
        try
        {
            Console.WriteLine($"[INFO] 开始执行多集合语义搜索，查询: '{query}', 代码库: '{codebasePath}'");
            
            if (_searchService == null || _configManager == null)
            {
                return "❌ 服务未初始化，请重启MCP服务器";
            }

            // 验证参数
            if (string.IsNullOrWhiteSpace(query))
            {
                return "❌ 请提供有效的搜索查询";
            }

            if (string.IsNullOrWhiteSpace(codebasePath))
            {
                return "❌ 请提供要搜索的代码库路径";
            }

            // 标准化路径
            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(codebasePath);
            }
            catch (Exception ex)
            {
                return $"❌ 无效的路径格式: {ex.Message}";
            }

            // 从配置中获取对应的集合名称
            var mapping = _configManager.GetMappingByPath(normalizedPath);
            if (mapping == null)
            {
                return $"📋 代码库未建立索引\n" +
                       $"📁 路径: {normalizedPath}\n" +
                       $"\n" +
                       $"❓ 是否为此代码库创建索引库？\n" +
                       $"✅ 创建后可立即进行语义搜索\n" +
                       $"🔍 请使用 CreateIndexLibrary 工具创建索引，参数：\n" +
                       $"   - codebasePath: {normalizedPath}\n" +
                       $"   - friendlyName: {Path.GetFileName(normalizedPath)} (可选)\n" +
                       $"\n" +
                       $"💡 创建完成后，重新执行此搜索即可获得结果";
            }

            // 检查索引状态
            if (mapping.IndexingStatus != "completed")
            {
                return $"❌ 代码库索引未完成\n" +
                       $"📁 代码库: {mapping.FriendlyName}\n" +
                       $"📊 当前状态: {mapping.IndexingStatus}\n" +
                       $"💡 请等待索引完成后再进行搜索，使用 GetIndexingStatus 工具查看进度";
            }

            Console.WriteLine($"[INFO] 找到映射: {mapping.FriendlyName} -> {mapping.CollectionName}");
            
            // 执行搜索
            var results = await _searchService.SearchAsync(query, mapping.CollectionName, limit);
            
            if (!results.Any())
            {
                return $"🔍 在代码库 '{mapping.FriendlyName}' 中未找到与查询 '{query}' 相关的代码片段\n\n" +
                       $"📊 搜索信息:\n" +
                       $"  📁 代码库: {mapping.CodebasePath}\n" +
                       $"  📦 索引片段数: {mapping.Statistics.IndexedSnippets}\n" +
                       $"  📄 文件数: {mapping.Statistics.TotalFiles}\n\n" +
                       $"💡 建议:\n" +
                       $"  1. 尝试使用不同的关键词或描述\n" +
                       $"  2. 检查代码库是否包含相关功能\n" +
                       $"  3. 如果代码最近有更新，索引可能需要时间同步";
            }

            // 格式化搜索结果
            var resultBuilder = new StringBuilder();
            resultBuilder.AppendLine($"🔍 在代码库 '{mapping.FriendlyName}' 中搜索: '{query}'");
            resultBuilder.AppendLine($"📍 集合: {mapping.CollectionName}");
            resultBuilder.AppendLine($"📄 配置来源: codebase-indexes.json");
            resultBuilder.AppendLine();
            resultBuilder.AppendLine($"找到 {results.Count} 个相关代码片段:");
            resultBuilder.AppendLine();

            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                var snippet = result.Snippet;

                resultBuilder.AppendLine($"--- 结果 {i + 1} (相似度: {result.Score:F4}) ---");
                
                // 显示相对路径更友好
                var relativePath = snippet.FilePath.GetRelativePath(mapping.CodebasePath);
                resultBuilder.AppendLine($"📄 文件: {relativePath}");
                
                if (!string.IsNullOrEmpty(snippet.Namespace))
                    resultBuilder.AppendLine($"📦 命名空间: {snippet.Namespace}");
                
                if (!string.IsNullOrEmpty(snippet.ClassName))
                    resultBuilder.AppendLine($"🏷️ 类: {snippet.ClassName}");
                
                if (!string.IsNullOrEmpty(snippet.MethodName))
                    resultBuilder.AppendLine($"🔧 成员: {snippet.MethodName}");

                resultBuilder.AppendLine($"📍 位置: 第 {snippet.StartLine}-{snippet.EndLine} 行");
                resultBuilder.AppendLine();
                
                resultBuilder.AppendLine("```csharp");
                resultBuilder.AppendLine(snippet.Code);
                resultBuilder.AppendLine("```");
                
                if (i < results.Count - 1)
                    resultBuilder.AppendLine(); // 添加空行分隔
            }

            // 添加搜索统计信息
            resultBuilder.AppendLine();
            resultBuilder.AppendLine("📊 搜索统计:");
            resultBuilder.AppendLine($"  📦 总索引片段: {mapping.Statistics.IndexedSnippets}");
            resultBuilder.AppendLine($"  📄 总文件数: {mapping.Statistics.TotalFiles}");
            resultBuilder.AppendLine($"  🎯 匹配结果: {results.Count}/{limit}");
            resultBuilder.AppendLine($"  📅 索引更新: {mapping.Statistics.LastUpdateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知"}");

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
            
            return $"❌ 搜索过程中发生错误: {ex.Message}\n\n" +
                   $"🔧 请检查:\n" +
                   $"1. 代码库路径是否正确: {codebasePath}\n" +
                   $"2. Qdrant 服务是否正常运行\n" +
                   $"3. API 配置是否正确\n" +
                   $"4. 网络连接是否正常\n\n" +
                   $"💡 使用 GetIndexingStatus 工具查看索引库状态";
        }
    }

    /// <summary>
    /// 列出所有可搜索的代码库
    /// </summary>
    /// <returns>可搜索的代码库列表</returns>
    [McpServerTool, Description("列出所有已建立索引的代码库信息和统计数据，用于查看当前可搜索的代码库")]
    public static async Task<string> ListSearchableCodebases()
    {
        try
        {
            if (_configManager == null)
            {
                return "❌ 服务未初始化，请重启MCP服务器";
            }

            var allMappings = _configManager.GetAllMappings();
            var searchableMappings = allMappings.Where(m => m.IndexingStatus == "completed").ToList();

            var resultBuilder = new StringBuilder();
            resultBuilder.AppendLine("📚 可搜索的代码库列表");
            resultBuilder.AppendLine();

            if (!searchableMappings.Any())
            {
                resultBuilder.AppendLine("❌ 当前没有可搜索的代码库");
                resultBuilder.AppendLine();
                resultBuilder.AppendLine("💡 使用 CreateIndexLibrary 工具创建第一个索引库");
                resultBuilder.AppendLine("🔍 使用 GetIndexingStatus 工具查看所有索引状态");
            }
            else
            {
                resultBuilder.AppendLine($"找到 {searchableMappings.Count} 个可搜索的代码库:");
                resultBuilder.AppendLine();

                foreach (var mapping in searchableMappings.OrderBy(m => m.FriendlyName))
                {
                    resultBuilder.AppendLine($"✅ {mapping.FriendlyName}");
                    resultBuilder.AppendLine($"   📁 路径: {mapping.CodebasePath}");
                    resultBuilder.AppendLine($"   📊 集合: {mapping.CollectionName}");
                    resultBuilder.AppendLine($"   📦 代码片段: {mapping.Statistics.IndexedSnippets:N0}");
                    resultBuilder.AppendLine($"   📄 文件数: {mapping.Statistics.TotalFiles:N0}");
                    resultBuilder.AppendLine($"   👁️ 监控状态: {(mapping.IsMonitoring ? "✅ 启用" : "⏸️ 禁用")}");
                    resultBuilder.AppendLine($"   📅 最后更新: {mapping.Statistics.LastUpdateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知"}");
                    resultBuilder.AppendLine();
                }

                resultBuilder.AppendLine("🔍 使用方法:");
                resultBuilder.AppendLine("  使用 SemanticCodeSearch 工具搜索代码");
                resultBuilder.AppendLine("  参数 codebasePath 填写上述任一路径即可");
            }

            return resultBuilder.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 列出代码库时发生错误: {ex.Message}");
            return $"❌ 列出代码库时发生错误: {ex.Message}";
        }
    }
}