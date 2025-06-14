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
    [McpServerTool, Description("🔍 智能代码片段搜索工具 - 根据自然语言描述精准定位相关代码片段，避免遍历读取整个文件。通过语义搜索直接获取目标代码段及其上下文信息，大幅提升代码查找效率。特别适用于：查找特定功能实现、定位错误代码、理解代码逻辑、获取代码示例等场景。如果代码库未建立索引，会提示创建索引库。")]
    public static async Task<string> SemanticCodeSearch(
        [Description("🎯 自然语言搜索查询 - 使用描述性语言精确表达要查找的代码功能。高效查询示例：'用户登录验证逻辑'、'数据库连接池管理'、'文件上传错误处理'、'JWT令牌生成'、'配置文件读取'、'异步任务处理'、'缓存机制实现'、'日志记录功能'、'API错误响应'、'数据验证规则'等。避免使用过于宽泛的查询如'函数'、'类'等。")] string query,
        [Description("📁 代码库路径 - 要搜索的代码库根目录路径。通常使用当前工作目录。支持格式：'d:/VSProject/MyApp'、'C:\\Projects\\MyProject'、'./src'等。系统会自动标准化路径格式。")] string codebasePath,
        [Description("📊 结果数量限制 - 返回最相关的代码片段数量，默认5个。建议：快速查找用5-10个，详细分析用15-20个，全面了解用25-30个。")] int limit = 5)
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
                       $"  📦 索引片段数: {mapping.Statistics.IndexedSnippets:N0}\n" +
                       $"  📄 文件数: {mapping.Statistics.TotalFiles:N0}\n\n" +
                       $"💡 优化搜索建议:\n" +
                       $"  🎯 使用更具体的功能描述，如'用户注册验证'而非'验证'\n" +
                       $"  🔄 尝试不同的表达方式，如'错误处理'、'异常捕获'、'错误管理'\n" +
                       $"  🏷️ 包含技术关键词，如'JWT认证'、'数据库连接池'、'HTTP请求'\n" +
                       $"  📝 描述具体行为，如'文件上传失败处理'、'用户权限检查'\n" +
                       $"  🔍 如果功能确实存在，可能需要更新索引或检查代码是否最近有变更";
            }

            // 格式化搜索结果
            var resultBuilder = new StringBuilder();
            resultBuilder.AppendLine($"🎯 智能代码搜索结果 - 查询: '{query}'");
            resultBuilder.AppendLine($"📁 代码库: {mapping.FriendlyName}");
            resultBuilder.AppendLine($"📍 索引集合: {mapping.CollectionName}");
            resultBuilder.AppendLine();
            resultBuilder.AppendLine($"✅ 找到 {results.Count} 个精准匹配的代码片段（按相关度排序）:");
            resultBuilder.AppendLine($"💡 这些片段已包含完整上下文，无需再读取整个文件");
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

            // 添加搜索统计信息和使用指导
            resultBuilder.AppendLine();
            resultBuilder.AppendLine("📊 搜索统计信息:");
            resultBuilder.AppendLine($"  📦 总索引片段: {mapping.Statistics.IndexedSnippets:N0}");
            resultBuilder.AppendLine($"  📄 总文件数: {mapping.Statistics.TotalFiles:N0}");
            resultBuilder.AppendLine($"  🎯 匹配结果: {results.Count}/{limit}");
            resultBuilder.AppendLine($"  📅 索引更新: {mapping.Statistics.LastUpdateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知"}");
            resultBuilder.AppendLine();
            resultBuilder.AppendLine("💡 使用建议:");
            resultBuilder.AppendLine("  ✅ 上述代码片段已包含完整的上下文信息，可直接分析使用");
            resultBuilder.AppendLine("  🚀 相比读取整个文件，语义搜索更精准高效");
            resultBuilder.AppendLine("  🔍 如需查找其他相关功能，请使用不同的查询词重新搜索");
            resultBuilder.AppendLine("  📝 如需了解更多上下文，可基于文件路径和行号进行定向查看");

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
                   $"🛠️ 故障排除:\n" +
                   $" 使用 GetIndexingStatus 工具查看索引库状态\n" +
                   $"📚 使用 ListSearchableCodebases 工具查看可用代码库\n" +
                   $"🔄 如果索引损坏，可使用 RebuildIndex 工具重建索引\n\n" +
                   $"⚡ 提示: SemanticCodeSearch 提供比文件遍历更高效的代码查找方式";
        }
    }

    /// <summary>
    /// 列出所有可搜索的代码库
    /// </summary>
    /// <returns>可搜索的代码库列表</returns>
    [McpServerTool, Description("📚 列出可搜索代码库 - 显示所有已建立语义索引的代码库信息和统计数据。使用此工具查看当前可用的代码库，然后选择合适的代码库路径用于 SemanticCodeSearch 工具进行智能代码搜索。")]
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

                resultBuilder.AppendLine("🚀 快速开始语义搜索:");
                resultBuilder.AppendLine("  1️⃣ 复制上述任一代码库路径");
                resultBuilder.AppendLine("  2️⃣ 使用 SemanticCodeSearch 工具，填入路径和自然语言查询");
                resultBuilder.AppendLine("  3️⃣ 获得精准的代码片段，无需遍历整个文件");
                resultBuilder.AppendLine();
                resultBuilder.AppendLine("💡 搜索技巧:");
                resultBuilder.AppendLine("  🎯 使用具体功能描述：'用户登录验证' 而非 '登录'");
                resultBuilder.AppendLine("  🔧 包含技术细节：'JWT令牌解析' 而非 '令牌'");
                resultBuilder.AppendLine("  📝 描述具体行为：'文件上传错误处理' 而非 '错误'");
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