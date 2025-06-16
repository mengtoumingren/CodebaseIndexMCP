using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using CodebaseMcpServer.Services;
using CodebaseMcpServer.Extensions;
using CodebaseMcpServer.Models;

namespace CodebaseMcpServer.Tools;

/// <summary>
/// 索引管理 MCP 工具
/// </summary>
[McpServerToolType]
public sealed class IndexManagementTools
{
    private static IndexingTaskManager? _taskManager;
    private static IndexConfigManager? _configManager;
    
    /// <summary>
    /// 初始化工具依赖
    /// </summary>
    public static void Initialize(IndexingTaskManager taskManager, IndexConfigManager configManager)
    {
        _taskManager = taskManager;
        _configManager = configManager;
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
        try
        {
            Console.WriteLine($"[INFO] 开始创建索引库，代码库路径: '{codebasePath}'");
            
            if (_taskManager == null || _configManager == null)
            {
                return "❌ 服务未初始化，请重启MCP服务器";
            }

            // 验证路径
            if (string.IsNullOrWhiteSpace(codebasePath))
            {
                return "❌ 请提供有效的代码库路径";
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

            // 检查目录是否存在
            if (!Directory.Exists(normalizedPath))
            {
                return $"❌ 指定的目录不存在: {normalizedPath}";
            }

            // 检查是否已存在索引
            var existingMapping = _configManager.GetMappingByPath(normalizedPath);
            if (existingMapping != null)
            {
                return $"❌ 该代码库已存在索引\n" +
                       $"📁 路径: {existingMapping.CodebasePath}\n" +
                       $"🏷️ 名称: {existingMapping.FriendlyName}\n" +
                       $"📊 集合: {existingMapping.CollectionName}\n" +
                       $"📅 创建时间: {existingMapping.CreatedAt:yyyy-MM-dd HH:mm:ss}\n" +
                       $"🔍 可直接使用 SemanticCodeSearch 工具搜索此代码库";
            }

            // 生成集合名称
            var collectionName = normalizedPath.GenerateCollectionName();
            var finalFriendlyName = friendlyName ?? Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar));

            Console.WriteLine($"[INFO] 生成集合名称: {collectionName}");
            Console.WriteLine($"[INFO] 友好名称: {finalFriendlyName}");

            // 启动索引任务
            var result = await _taskManager.StartIndexingAsync(normalizedPath, finalFriendlyName);
            
            if (!result.Success)
            {
                return $"❌ 索引任务启动失败: {result.Message}";
            }

            // 构建成功响应
            var response = new StringBuilder();
            response.AppendLine("✅ 索引库创建任务已启动！");
            response.AppendLine();
            response.AppendLine($"📁 代码库路径: {normalizedPath}");
            response.AppendLine($"🏷️ 友好名称: {finalFriendlyName}");
            response.AppendLine($"📊 集合名称: {collectionName}");
            response.AppendLine($"🆔 任务ID: {result.TaskId}");
            response.AppendLine();
            response.AppendLine("🔄 索引进度:");
            response.AppendLine("  - 正在扫描C#文件...");
            response.AppendLine("  - 正在提取代码片段...");
            response.AppendLine("  - 正在生成向量索引...");
            response.AppendLine();
            response.AppendLine("⏳ 索引完成后将自动启用以下功能:");
            response.AppendLine("  🔍 SemanticCodeSearch - 语义代码搜索");
            response.AppendLine("  👁️ 文件监控 - 自动更新索引");
            response.AppendLine("  📄 配置保存到: codebase-indexes.json");
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
        try
        {
            if (_taskManager == null || _configManager == null)
            {
                return "❌ 服务未初始化，请重启MCP服务器";
            }

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

                var mapping = _configManager.GetMappingByPath(normalizedPath);
                if (mapping == null)
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
                    response.AppendLine($"📁 路径: {mapping.CodebasePath}");
                    response.AppendLine($"🏷️ 名称: {mapping.FriendlyName}");
                    response.AppendLine($"📊 集合: {mapping.CollectionName}");
                    response.AppendLine($"📊 状态: {GetMappingStatusEmoji(mapping.IndexingStatus)} {mapping.IndexingStatus}");
                    response.AppendLine($"📦 代码片段: {mapping.Statistics.IndexedSnippets:N0}");
                    response.AppendLine($"📄 文件数: {mapping.Statistics.TotalFiles:N0}");
                    response.AppendLine($"👁️ 监控状态: {(mapping.IsMonitoring ? "✅ 启用" : "⏸️ 禁用")}");
                    response.AppendLine($"📅 创建时间: {mapping.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                    response.AppendLine($"📅 最后更新: {mapping.Statistics.LastUpdateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知"}");
                    
                    // 查找相关的运行中任务
                    var runningTasks = _taskManager.GetRunningTasks()
                        .Where(t => Path.GetFullPath(t.CodebasePath) == normalizedPath)
                        .ToList();
                    
                    if (runningTasks.Any())
                    {
                        response.AppendLine();
                        response.AppendLine("🔄 运行中的任务:");
                        foreach (var task in runningTasks)
                        {
                            response.AppendLine($"  📋 {task.Id[..8]}... - {task.Status} ({task.ProgressPercentage:F1}%)");
                        }
                    }
                }
            }
            else if (!string.IsNullOrEmpty(taskId))
            {
                // 查询特定任务状态
                var task = _taskManager.GetTaskStatus(taskId);
                if (task == null)
                {
                    response.AppendLine($"❌ 未找到任务ID: {taskId}");
                    response.AppendLine();
                    response.AppendLine("💡 提示: 使用不带参数的 GetIndexingStatus 查看所有索引状态");
                }
                else
                {
                    response.AppendLine($"📋 任务状态详情 (ID: {taskId})");
                    response.AppendLine();
                    response.AppendLine($"📁 代码库: {task.CodebasePath}");
                    response.AppendLine($"📊 状态: {GetStatusEmoji(task.Status)} {task.Status}");
                    response.AppendLine($"⏱️ 开始时间: {task.StartTime:yyyy-MM-dd HH:mm:ss}");
                    
                    if (task.EndTime.HasValue)
                    {
                        response.AppendLine($"⏱️ 结束时间: {task.EndTime:yyyy-MM-dd HH:mm:ss}");
                        response.AppendLine($"⏱️ 耗时: {(task.EndTime - task.StartTime)?.TotalSeconds:F1}秒");
                    }
                    
                    response.AppendLine($"📈 进度: {task.ProgressPercentage:F1}%");
                    
                    if (!string.IsNullOrEmpty(task.CurrentFile))
                    {
                        response.AppendLine($"📄 当前: {task.CurrentFile}");
                    }
                    
                    if (task.IndexedCount > 0)
                    {
                        response.AppendLine($"📦 已索引片段: {task.IndexedCount}");
                    }
                    
                    if (!string.IsNullOrEmpty(task.ErrorMessage))
                    {
                        response.AppendLine($"❌ 错误信息: {task.ErrorMessage}");
                    }
                }
            }
            else
            {
                // 显示所有索引状态
                var allMappings = _configManager.GetAllMappings();
                var runningTasks = _taskManager.GetRunningTasks();
                var statistics = await _taskManager.GetIndexingStatistics();

                response.AppendLine("📊 索引库状态总览");
                response.AppendLine();
                
                // 全局统计
                response.AppendLine("🌍 全局统计:");
                var stats = statistics as dynamic;
                response.AppendLine($"  📁 总代码库数: {stats?.TotalCodebases ?? 0}");
                response.AppendLine($"  ✅ 已完成索引: {stats?.CompletedIndexes ?? 0}");
                response.AppendLine($"  ❌ 索引失败: {stats?.FailedIndexes ?? 0}");
                response.AppendLine($"  🔄 运行中任务: {stats?.RunningTasks ?? 0}");
                response.AppendLine($"  📦 总代码片段: {stats?.TotalSnippets ?? 0}");
                response.AppendLine($"  📄 总文件数: {stats?.TotalFiles ?? 0}");
                response.AppendLine($"  👁️ 监控中代码库: {stats?.MonitoredCodebases ?? 0}");
                response.AppendLine();

                // 运行中的任务
                if (runningTasks.Any())
                {
                    response.AppendLine("🔄 运行中的任务:");
                    foreach (var task in runningTasks)
                    {
                        response.AppendLine($"  📋 {task.Id[..8]}... - {Path.GetFileName(task.CodebasePath)} ({task.ProgressPercentage:F1}%)");
                    }
                    response.AppendLine();
                }

                // 已建立的索引库
                if (allMappings.Any())
                {
                    response.AppendLine("📚 已建立的索引库:");
                    foreach (var mapping in allMappings.OrderByDescending(m => m.LastIndexed ?? m.CreatedAt))
                    {
                        var statusEmoji = GetMappingStatusEmoji(mapping.IndexingStatus);
                        response.AppendLine($"  {statusEmoji} {mapping.FriendlyName}");
                        response.AppendLine($"    📁 路径: {mapping.CodebasePath}");
                        response.AppendLine($"    📊 集合: {mapping.CollectionName}");
                        response.AppendLine($"    📦 片段数: {mapping.Statistics.IndexedSnippets}");
                        response.AppendLine($"    📄 文件数: {mapping.Statistics.TotalFiles}");
                        response.AppendLine($"    👁️ 监控: {(mapping.IsMonitoring ? "启用" : "禁用")}");
                        response.AppendLine($"    📅 最后更新: {mapping.Statistics.LastUpdateTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "未知"}");
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

    private static string GetStatusEmoji(IndexingStatus status)
    {
        return status switch
        {
            IndexingStatus.Pending => "⏳",
            IndexingStatus.Running => "🔄",
            IndexingStatus.Completed => "✅",
            IndexingStatus.Failed => "❌",
            IndexingStatus.Cancelled => "🚫",
            _ => "❓"
        };
    }

    private static string GetMappingStatusEmoji(string status)
    {
        return status switch
        {
            "completed" => "✅",
            "indexing" => "🔄",
            "failed" => "❌",
            "pending" => "⏳",
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
        try
        {
            if (_taskManager == null || _configManager == null)
            {
                return "❌ 服务未初始化，请重启MCP服务器";
            }

            var result = await _taskManager.RebuildIndexAsync(codebasePath);
            
            if (result.Success)
            {
                return $"✅ 索引重建任务已启动\n" +
                       $"📁 代码库: {codebasePath}\n" +
                       $"🆔 任务ID: {result.TaskId}\n" +
                       $"💡 使用 GetIndexingStatus 工具查看进度";
            }
            else
            {
                return $"❌ 重建索引失败: {result.Message}";
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
        try
        {
            Console.WriteLine($"[INFO] 开始执行删除索引库，代码库路径: '{codebasePath}', 确认标志: {confirm}");
            
            if (_taskManager == null || _configManager == null)
            {
                return "❌ 服务未初始化，请重启MCP服务器";
            }

            // 验证路径
            if (string.IsNullOrWhiteSpace(codebasePath))
            {
                return "❌ 请提供有效的代码库路径";
            }

            string normalizedPath;
            try
            {
                normalizedPath = Path.GetFullPath(codebasePath);
            }
            catch (Exception ex)
            {
                return $"❌ 无效的路径格式: {ex.Message}";
            }

            // 执行删除
            var result = await _taskManager.DeleteIndexLibraryAsync(normalizedPath, confirm);
            
            if (result.Success || !confirm)
            {
                // 成功删除或显示确认信息
                Console.WriteLine($"[INFO] 删除索引库操作完成，成功: {result.Success}");
                return result.Message;
            }
            else
            {
                Console.WriteLine($"[ERROR] 删除索引库失败: {result.Message}");
                return result.Message;
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
                   $"3. 配置文件访问权限是否正常\n" +
                   $"4. 任务持久化目录是否可写\n\n" +
                   $"🛠️ 故障排除:\n" +
                   $"💡 使用 GetIndexingStatus 工具查看索引库状态\n" +
                   $"🔍 检查服务器日志获取详细错误信息\n" +
                   $"🔄 如果部分删除成功，可能需要手动清理残留数据\n\n" +
                   $"⚡ 提示: DeleteIndexLibrary 提供安全的索引库完整删除功能";
        }
    }
}