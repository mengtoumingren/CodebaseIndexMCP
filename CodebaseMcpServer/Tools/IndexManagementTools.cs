using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using CodebaseMcpServer.Services;
using CodebaseMcpServer.Extensions;
using CodebaseMcpServer.Models;
using CodebaseMcpServer.Services.Domain;

namespace CodebaseMcpServer.Tools;

/// <summary>
/// 索引管理 MCP 工具
/// </summary>
[McpServerToolType]
public sealed class IndexManagementTools
{
    private static IServiceProvider? _serviceProvider;
    
    /// <summary>
    /// 初始化工具依赖
    /// </summary>
    public static void Initialize(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 创建索引库工具 - 为指定的代码库目录创建索引库
    /// </summary>
    /// <param name="codebasePath">要创建索引的代码库目录的完整绝对路径</param>
    /// <param name="friendlyName">可选的索引库友好名称，如果不提供则使用目录名</param>
    /// <returns>索引创建结果</returns>
    [McpServerTool, Description("为代码库创建语义搜索索引，创建后可使用 SemanticCodeSearch 进行智能代码搜索。支持自动文件监控和索引更新。")]
    public static async Task<string> CreateIndexLibrary(
        [Description("代码库目录的完整路径，通常是当前工作目录，例如：'d:/VSProject/MyApp' 或 'C:\\Projects\\MyProject'")] string codebasePath,
        [Description("可选的索引库友好名称，如果不提供则使用目录名")] string? friendlyName = null)
    {
        if (_serviceProvider == null)
        {
            return "❌ 服务未初始化，请重启MCP服务器";
        }

        using var scope = _serviceProvider.CreateScope();
        var indexLibraryService = scope.ServiceProvider.GetRequiredService<IIndexLibraryService>();

        try
        {
            Console.WriteLine($"[INFO] 开始创建索引库，代码库路径: '{codebasePath}'");

            var request = new CodebaseMcpServer.Models.Domain.CreateLibraryRequest
            {
                CodebasePath = codebasePath,
                Name = friendlyName,
                PresetIds = null // MCP工具暂不支持预设
            };

            var result = await indexLibraryService.CreateAsync(request);

            if (!result.IsSuccess)
            {
                return $"❌ 索引库创建失败: {result.Message}";
            }
            
            var library = result.Library!;
            
            // 构建成功响应
            var response = new StringBuilder();
            response.AppendLine("✅ 索引库创建任务已启动！");
            response.AppendLine();
            response.AppendLine($"📁 代码库路径: {library.CodebasePath}");
            response.AppendLine($"🏷️ 友好名称: {library.Name}");
            response.AppendLine($"📊 集合名称: {library.CollectionName}");
            response.AppendLine($"🆔 任务ID: {result.TaskId}");
            response.AppendLine();
            response.AppendLine("🔄 索引进度:");
            response.AppendLine("  - 正在扫描文件...");
            response.AppendLine("  - 正在提取代码片段...");
            response.AppendLine("  - 正在生成向量索引...");
            response.AppendLine();
            response.AppendLine("⏳ 索引完成后将自动启用以下功能:");
            response.AppendLine("  🔍 SemanticCodeSearch - 语义代码搜索");
            response.AppendLine("  👁️ 文件监控 - 自动更新索引");
            response.AppendLine();
            response.AppendLine("💡 提示: 可使用 GetIndexingStatus 工具查看索引进度");

            Console.WriteLine($"[INFO] 索引任务创建成功，任务ID: {result.TaskId}");
            return response.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 创建索引库时发生错误: {ex.Message}");
            Console.WriteLine($"[ERROR] 异常类型: {ex.GetType().Name}");
            Console.WriteLine($"[ERROR] 堆栈跟踪: {ex.StackTrace}");
            
            return $"❌ 创建索引库时发生错误: {ex.Message}\n\n" +
                   "🔧 请检查:\n" +
                   "1. 路径是否正确且可访问\n" +
                   "2. Qdrant 服务是否正常运行\n" +
                   "3. API 配置是否正确\n" +
                   "4. 磁盘空间是否足够";
        }
    }

    /// <summary>
    /// 查询索引状态工具
    /// </summary>
    /// <param name="taskId">可选的任务ID，如果不提供则显示所有索引状态</param>
    /// <returns>索引状态信息</returns>
    [McpServerTool, Description("查看索引库状态和统计信息，可以查看特定代码库或所有索引库的状态")]
    public static async Task<string> GetIndexingStatus(
        [Description("可选的代码库路径，如果提供则查看该代码库的索引状态")] string? codebasePath = null,
        [Description("可选的任务ID，如果提供则查看特定任务状态")] string? taskId = null)
    {
        if (_serviceProvider == null)
        {
            return "❌ 服务未初始化，请重启MCP服务器";
        }

        using var scope = _serviceProvider.CreateScope();
        var indexLibraryService = scope.ServiceProvider.GetRequiredService<IIndexLibraryService>();

        try
        {
            var response = new StringBuilder();

            if (!string.IsNullOrEmpty(codebasePath))
            {
                // 查询特定代码库状态
                string normalizedPath;
                try
                {
                    normalizedPath = Path.GetFullPath(codebasePath);
                }
                catch (Exception ex)
                {
                    return $"❌ 无效的路径格式: {ex.Message}";
                }

                var library = await indexLibraryService.GetByPathAsync(normalizedPath);
                if (library == null)
                {
                    response.AppendLine($"📋 代码库索引状态");
                    response.AppendLine($"📁 路径: {normalizedPath}");
                    response.AppendLine($"📊 状态: ❌ 未建立索引");
                    response.AppendLine();
                    response.AppendLine($"💡 使用 CreateIndexLibrary 工具为此代码库创建索引");
                }
                else
                {
                    response.AppendLine($"📋 代码库索引状态");
                    response.AppendLine($"📁 路径: {library.CodebasePath}");
                    response.AppendLine($"🏷️ 名称: {library.Name}");
                    response.AppendLine($"📊 集合: {library.CollectionName}");
                    response.AppendLine($"📊 状态: {GetMappingStatusEmoji(library.Status.ToString().ToLower())} {library.Status}");
                    response.AppendLine($"📦 代码片段: {library.IndexedSnippets:N0}");
                    response.AppendLine($"📄 文件数: {library.TotalFiles:N0}");
                    response.AppendLine($"👁️ 监控状态: {(library.WatchConfigObject.IsEnabled ? "✅ 启用" : "⏸️ 禁用")}");
                    response.AppendLine($"📅 创建时间: {library.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                    response.AppendLine($"📅 最后更新: {library.LastIndexedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知"}");
                }
            }
            else
            {
                // 显示所有索引状态
                var allLibraries = await indexLibraryService.GetAllAsync();
                var globalStats = await indexLibraryService.GetGlobalStatisticsAsync();

                response.AppendLine("📊 索引库状态总览");
                response.AppendLine();
                
                // 全局统计
                response.AppendLine("🌍 全局统计:");
                response.AppendLine($"  📁 总代码库数: {globalStats.TotalLibraries}");
                response.AppendLine($"  📦 总代码片段: {globalStats.TotalIndexedSnippets:N0}");
                response.AppendLine($"  📄 总文件数: {globalStats.TotalFiles:N0}");
                response.AppendLine();

                // 已建立的索引库
                if (allLibraries.Any())
                {
                    response.AppendLine("📚 已建立的索引库:");
                    foreach (var library in allLibraries.OrderByDescending(l => l.LastIndexedAt ?? l.CreatedAt))
                    {
                        var statusEmoji = GetMappingStatusEmoji(library.Status.ToString());
                        response.AppendLine($"  {statusEmoji} {library.Name}");
                        response.AppendLine($"    📁 路径: {library.CodebasePath}");
                        response.AppendLine($"    📊 集合: {library.CollectionName}");
                        response.AppendLine($"    📦 片段数: {library.IndexedSnippets}");
                        response.AppendLine($"    📄 文件数: {library.TotalFiles}");
                        response.AppendLine($"    👁️ 监控: {(library.WatchConfigObject.IsEnabled ? "启用" : "禁用")}");
                        response.AppendLine($"    📅 最后更新: {library.LastIndexedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知"}");
                        response.AppendLine();
                    }
                }
                else
                {
                    response.AppendLine("📚 暂未建立任何索引库");
                    response.AppendLine();
                    response.AppendLine("💡 使用 CreateIndexLibrary 工具创建第一个索引库");
                }
            }

            return response.ToString();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 查询索引状态时发生错误: {ex.Message}");
            return $"❌ 查询索引状态时发生错误: {ex.Message}";
        }
    }

    private static string GetMappingStatusEmoji(string status)
    {
        return status.ToLower() switch
        {
            "completed" => "✅",
            "indexing" => "🔄",
            "running" => "🔄",
            "failed" => "❌",
            "pending" => "⏳",
            "cancelled" => "🚫",
            _ => "❓"
        };
    }

    /// <summary>
    /// 重建索引工具
    /// </summary>
    /// <param name="codebasePath">要重建索引的代码库路径</param>
    /// <returns>重建结果</returns>
    [McpServerTool, Description("重建代码库索引，清除现有索引数据并重新创建，用于解决索引损坏或需要完全更新的情况")]
    public static async Task<string> RebuildIndex(
        [Description("要重建索引的代码库路径")] string codebasePath)
    {
        if (_serviceProvider == null)
        {
            return "❌ 服务未初始化，请重启MCP服务器";
        }

        using var scope = _serviceProvider.CreateScope();
        var indexLibraryService = scope.ServiceProvider.GetRequiredService<IIndexLibraryService>();

        try
        {
            var library = await indexLibraryService.GetByPathAsync(codebasePath);
            if (library == null)
            {
                return $"❌ 未找到代码库: {codebasePath}";
            }

            var taskId = await indexLibraryService.RebuildIndexAsync(library.Id);

            if (!string.IsNullOrEmpty(taskId))
            {
                return $"✅ 索引重建任务已启动\n" +
                       $"📁 代码库: {codebasePath}\n" +
                       $"🆔 任务ID: {taskId}\n" +
                       $"💡 使用 GetIndexingStatus 工具查看进度";
            }
            else
            {
                return $"❌ 重建索引失败";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 重建索引时发生错误: {ex.Message}");
            return $"❌ 重建索引时发生错误: {ex.Message}";
        }
    }

    /// <summary>
    /// 删除索引库工具 - 安全确认模式
    /// </summary>
    /// <param name="codebasePath">要删除索引的代码库路径</param>
    /// <param name="confirm">确认删除标志，设为true表示确认执行删除操作</param>
    /// <returns>删除结果</returns>
    [McpServerTool, Description("删除代码库索引，完全移除指定代码库的索引数据和配置。删除前会显示详细信息供确认。")]
    public static async Task<string> DeleteIndexLibrary(
        [Description("要删除索引的代码库路径")] string codebasePath,
        [Description("确认删除标志，设为true表示确认执行删除操作")] bool confirm = false)
    {
        if (_serviceProvider == null)
        {
            return "❌ 服务未初始化，请重启MCP服务器";
        }

        using var scope = _serviceProvider.CreateScope();
        var indexLibraryService = scope.ServiceProvider.GetRequiredService<IIndexLibraryService>();

        try
        {
            Console.WriteLine($"[INFO] 开始执行删除索引库，代码库路径: '{codebasePath}', 确认标志: {confirm}");
            
            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(codebasePath);
            }
            catch (Exception ex)
            {
                return $"❌ 无效的路径格式: {ex.Message}";
            }

            var library = await indexLibraryService.GetByPathAsync(normalizedPath);
            if (library == null)
            {
                return $"❌ 未找到与路径 '{normalizedPath}' 关联的索引库。";
            }

            if (!confirm)
            {
                return $"⚠️ 确认删除索引库 '{library.Name}'？\n" +
                       $"此操作将永久删除集合 '{library.CollectionName}' 及其所有数据。\n" +
                       $"要确认删除，请重新运行此命令并设置 'confirm' 参数为 true。";
            }

            var success = await indexLibraryService.DeleteAsync(library.Id);

            if (success)
            {
                return $"✅ 成功删除索引库 '{library.Name}'。";
            }
            else
            {
                return $"❌ 删除索引库 '{library.Name}' 失败。请检查日志获取详细信息。";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 删除索引库时发生错误: {ex.Message}");
            Console.WriteLine($"[ERROR] 异常类型: {ex.GetType().Name}");
            Console.WriteLine($"[ERROR] 堆栈跟踪: {ex.StackTrace}");
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"[ERROR] 内部异常: {ex.InnerException.GetType().Name}");
                Console.WriteLine($"[ERROR] 内部异常消息: {ex.InnerException.Message}");
            }
            
            return $"❌ 删除索引库时发生错误: {ex.Message}\n\n" +
                   $"🔧 请检查:\n" +
                   $"1. 代码库路径是否正确: {codebasePath}\n" +
                   $"2. Qdrant 服务是否正常运行\n" +
                   $"3. 数据库连接是否正常\n\n" +
                   $"🛠️ 故障排除:\n" +
                   $"💡 使用 GetIndexingStatus 工具查看索引库状态\n" +
                   $"🔍 检查服务器日志获取详细错误信息";
        }
    }
}