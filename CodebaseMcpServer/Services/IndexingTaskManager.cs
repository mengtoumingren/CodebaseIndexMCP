using System.Collections.Concurrent;
using CodebaseMcpServer.Models;
using CodebaseMcpServer.Extensions;

namespace CodebaseMcpServer.Services;

/// <summary>
/// 索引任务管理器 - 管理代码库索引任务的创建和执行
/// </summary>
public class IndexingTaskManager
{
    private readonly ConcurrentDictionary<string, IndexingTask> _runningTasks = new();
    private readonly ILogger<IndexingTaskManager> _logger;
    private readonly IndexConfigManager _configManager;
    private readonly EnhancedCodeSemanticSearch _searchService;
    private readonly IConfiguration _configuration;
    private readonly TaskPersistenceService _persistenceService;
    private readonly QdrantConnectionMonitor _connectionMonitor;

    public IndexingTaskManager(
        ILogger<IndexingTaskManager> logger,
        IndexConfigManager configManager,
        EnhancedCodeSemanticSearch searchService,
        IConfiguration configuration,
        TaskPersistenceService persistenceService,
        QdrantConnectionMonitor connectionMonitor)
    {
        _logger = logger;
        _configManager = configManager;
        _searchService = searchService;
        _configuration = configuration;
        _persistenceService = persistenceService;
        _connectionMonitor = connectionMonitor;
        
        // 启动时恢复未完成的任务
        _ = Task.Run(RestorePendingTasksAsync);
    }

    /// <summary>
    /// 恢复未完成的任务
    /// </summary>
    private async Task RestorePendingTasksAsync()
    {
        try
        {
            _logger.LogInformation("开始恢复未完成的索引任务...");
            var pendingTasks = await _persistenceService.LoadPendingTasksAsync();
            
            foreach (var task in pendingTasks)
            {
                var normalizedPath = task.CodebasePath.NormalizePath();
                if (!_runningTasks.ContainsKey(normalizedPath))
                {
                    _runningTasks.TryAdd(normalizedPath, task);
                    
                    // 异步恢复执行任务
                    _ = Task.Run(async () => await ExecuteIndexingTaskAsync(task, null));
                    
                    _logger.LogInformation("恢复索引任务: {Path}, 任务ID: {TaskId}", task.CodebasePath, task.Id);
                }
            }
            
            _logger.LogInformation("任务恢复完成，共恢复 {Count} 个任务", pendingTasks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复未完成任务失败");
        }
    }

    /// <summary>
    /// 启动索引任务
    /// </summary>
    public async Task<IndexingResult> StartIndexingAsync(string codebasePath, string? friendlyName = null)
    {
        var normalizedPath = codebasePath.NormalizePath();
        
        // 检查是否已在执行
        if (_runningTasks.ContainsKey(normalizedPath))
        {
            var existingTask = _runningTasks[normalizedPath];
            return new IndexingResult
            {
                Success = false,
                Message = "该代码库正在索引中，请等待完成",
                TaskId = existingTask.Id
            };
        }

        // 检查路径是否有效
        if (!Directory.Exists(codebasePath))
        {
            return new IndexingResult
            {
                Success = false,
                Message = $"指定的代码库路径不存在: {codebasePath}"
            };
        }

        // 检查是否已存在映射
        var existingMapping = _configManager.GetMappingByPath(codebasePath);
        if (existingMapping != null)
        {
            return new IndexingResult
            {
                Success = false,
                Message = $"该代码库已存在索引: {existingMapping.FriendlyName} ({existingMapping.CollectionName})"
            };
        }

        // 创建索引任务
        var task = new IndexingTask
        {
            Id = PathExtensions.GenerateUniqueId(),
            CodebasePath = codebasePath,
            Status = IndexingStatus.Running,
            StartTime = DateTime.UtcNow,
            ProgressPercentage = 0
        };
        
        _runningTasks.TryAdd(normalizedPath, task);
        
        // 保存任务到本地存储
        await _persistenceService.SaveTaskAsync(task);
        
        // 异步执行索引
        _ = Task.Run(async () => await ExecuteIndexingTaskAsync(task, friendlyName));
        
        _logger.LogInformation("索引任务已启动: {Path}, 任务ID: {TaskId}", codebasePath, task.Id);
        
        return new IndexingResult
        {
            Success = true,
            Message = "索引任务已启动",
            TaskId = task.Id
        };
    }

    /// <summary>
    /// 执行索引任务
    /// </summary>
    private async Task ExecuteIndexingTaskAsync(IndexingTask task, string? friendlyName)
    {
        var normalizedPath = task.CodebasePath.NormalizePath();
        
        try
        {
            _logger.LogInformation("开始执行索引任务: {Path}", task.CodebasePath);
            task.CurrentFile = "正在初始化...";
            
            // 更新任务状态到持久化存储
            await _persistenceService.UpdateTaskAsync(task);
            
            // 检查Qdrant连接状态
            if (!_connectionMonitor.IsConnected)
            {
                _logger.LogWarning("Qdrant连接不可用，任务 {TaskId} 等待连接恢复", task.Id);
                task.CurrentFile = "等待数据库连接恢复...";
                task.Status = IndexingStatus.Pending;
                await _persistenceService.UpdateTaskAsync(task);
                
                // 等待连接恢复
                var connectionRestored = await _connectionMonitor.WaitForConnectionAsync(task.Id);
                if (!connectionRestored)
                {
                    throw new InvalidOperationException("等待Qdrant连接超时，任务被取消");
                }
                
                _logger.LogInformation("Qdrant连接已恢复，继续执行任务 {TaskId}", task.Id);
                task.Status = IndexingStatus.Running;
                task.CurrentFile = "连接已恢复，继续索引...";
                await _persistenceService.UpdateTaskAsync(task);
            }
            
            // 生成集合名称
            var collectionName = task.CodebasePath.GenerateCollectionName();
            
            // 创建代码库映射
            var mapping = new CodebaseMapping
            {
                Id = PathExtensions.GenerateUniqueId(),
                CodebasePath = task.CodebasePath,
                NormalizedPath = normalizedPath,
                CollectionName = collectionName,
                FriendlyName = friendlyName ?? Path.GetFileName(task.CodebasePath.TrimEnd(Path.DirectorySeparatorChar)),
                CreatedAt = DateTime.UtcNow,
                IndexingStatus = "indexing",
                IsMonitoring = true
            };
            
            // 保存映射到配置
            var added = await _configManager.AddCodebaseMapping(mapping);
            if (!added)
            {
                throw new InvalidOperationException("无法保存代码库映射配置");
            }

            // 获取文件列表
            task.CurrentFile = "正在扫描文件...";
            await _persistenceService.UpdateTaskAsync(task);
            
            var codeFiles = Directory.GetFiles(task.CodebasePath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.IsExcludedPath(new List<string> { "bin", "obj", ".git", "node_modules" }))
                .ToList();

            mapping.Statistics.TotalFiles = codeFiles.Count;
            await _configManager.UpdateMapping(mapping);

            if (codeFiles.Count == 0)
            {
                throw new InvalidOperationException("在指定目录中未找到C#代码文件");
            }

            _logger.LogInformation("找到 {Count} 个C#文件，开始索引", codeFiles.Count);
            
            // 执行索引
            task.CurrentFile = "正在建立索引...";
            task.ProgressPercentage = 10;
            await _persistenceService.UpdateTaskAsync(task);
            
            // 检查连接状态后再执行索引
            if (!_connectionMonitor.IsConnected)
            {
                _logger.LogWarning("索引过程中Qdrant连接断开，等待恢复...");
                task.CurrentFile = "数据库连接断开，等待恢复...";
                await _persistenceService.UpdateTaskAsync(task);
                
                var connectionRestored = await _connectionMonitor.WaitForConnectionAsync(task.Id);
                if (!connectionRestored)
                {
                    throw new InvalidOperationException("索引过程中Qdrant连接超时");
                }
            }
            
            var indexedCount = await _searchService.ProcessCodebaseAsync(
                task.CodebasePath,
                collectionName,
                new List<string> { "*.cs" });
            
            // 更新任务状态
            task.Status = IndexingStatus.Completed;
            task.EndTime = DateTime.UtcNow;
            task.IndexedCount = indexedCount;
            task.ProgressPercentage = 100;
            task.CurrentFile = "索引完成";
            
            // 更新映射状态
            mapping.IndexingStatus = "completed";
            mapping.LastIndexed = DateTime.UtcNow;
            mapping.Statistics.IndexedSnippets = indexedCount;
            mapping.Statistics.LastIndexingDuration = $"{(task.EndTime - task.StartTime)?.TotalSeconds:F1}s";
            mapping.Statistics.LastUpdateTime = DateTime.UtcNow;
            
            await _configManager.UpdateMapping(mapping);
            
            // 清理已完成的任务
            await _persistenceService.CleanupTaskAsync(task.Id);
            
            _logger.LogInformation("索引任务完成: {Path}, 索引片段数: {Count}, 耗时: {Duration}s",
                task.CodebasePath, indexedCount, (task.EndTime - task.StartTime)?.TotalSeconds);
        }
        catch (Exception ex)
        {
            task.Status = IndexingStatus.Failed;
            task.ErrorMessage = ex.Message;
            task.EndTime = DateTime.UtcNow;
            task.CurrentFile = "索引失败";
            
            // 更新失败状态到持久化存储
            await _persistenceService.UpdateTaskAsync(task);
            
            _logger.LogError(ex, "索引任务失败: {Path}", task.CodebasePath);
            
            // 尝试更新映射状态为失败
            try
            {
                var mapping = _configManager.GetMappingByPath(task.CodebasePath);
                if (mapping != null)
                {
                    mapping.IndexingStatus = "failed";
                    await _configManager.UpdateMapping(mapping);
                }
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, "更新失败状态时出错");
            }
            
            // 延迟清理失败的任务（保留一段时间供查看）
            _ = Task.Delay(TimeSpan.FromHours(1)).ContinueWith(async _ =>
            {
                await _persistenceService.CleanupTaskAsync(task.Id);
            });
        }
        finally
        {
            _runningTasks.TryRemove(normalizedPath, out _);
        }
    }

    /// <summary>
    /// 获取任务状态
    /// </summary>
    public IndexingTask? GetTaskStatus(string taskId)
    {
        return _runningTasks.Values.FirstOrDefault(t => t.Id == taskId);
    }

    /// <summary>
    /// 获取所有运行中的任务
    /// </summary>
    public List<IndexingTask> GetRunningTasks()
    {
        return _runningTasks.Values.ToList();
    }

    /// <summary>
    /// 取消任务
    /// </summary>
    public async Task<bool> CancelTaskAsync(string taskId)
    {
        var task = _runningTasks.Values.FirstOrDefault(t => t.Id == taskId);
        if (task != null)
        {
            task.Status = IndexingStatus.Cancelled;
            task.EndTime = DateTime.UtcNow;
            task.CurrentFile = "任务已取消";
            
            // 取消等待连接的任务
            _connectionMonitor.CancelWaitingTask(taskId);
            
            // 更新持久化状态
            await _persistenceService.UpdateTaskAsync(task);
            
            // 延迟清理取消的任务
            _ = Task.Delay(TimeSpan.FromMinutes(10)).ContinueWith(async _ =>
            {
                await _persistenceService.CleanupTaskAsync(taskId);
            });
            
            _logger.LogInformation("取消索引任务: {TaskId}", taskId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 取消任务（保持向后兼容）
    /// </summary>
    public bool CancelTask(string taskId)
    {
        return CancelTaskAsync(taskId).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 重建索引
    /// </summary>
    public async Task<IndexingResult> RebuildIndexAsync(string codebasePath)
    {
        var mapping = _configManager.GetMappingByPath(codebasePath);
        if (mapping == null)
        {
            return new IndexingResult
            {
                Success = false,
                Message = "指定的代码库未建立索引"
            };
        }

        // 先删除现有映射
        await _configManager.RemoveMapping(mapping.Id);
        
        // 重新创建索引
        return await StartIndexingAsync(codebasePath, mapping.FriendlyName);
    }

    /// <summary>
    /// 更新单个文件的索引
    /// </summary>
    public async Task<bool> UpdateFileIndexAsync(string filePath, string collectionName)
    {
        try
        {
            if (!File.Exists(filePath) || !filePath.IsSupportedExtension(new List<string> { ".cs" }))
            {
                return false;
            }

            _logger.LogDebug("更新文件索引: {FilePath}", filePath);
            
            var snippets = _searchService.ExtractCSharpSnippets(filePath);
            if (snippets.Any())
            {
                await _searchService.BatchIndexSnippetsAsync(snippets, collectionName);
                _logger.LogInformation("文件索引更新完成: {FilePath}, 片段数: {Count}", filePath, snippets.Count);
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新文件索引失败: {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// 获取索引统计信息
    /// </summary>
    public async Task<object> GetIndexingStatistics()
    {
        var mappings = _configManager.GetAllMappings();
        var runningTasks = GetRunningTasks();
        var connectionStats = await _connectionMonitor.GetConnectionStatisticsAsync();
        var storageStats = await _persistenceService.GetStorageStatisticsAsync();
        
        return new
        {
            TotalCodebases = mappings.Count,
            CompletedIndexes = mappings.Count(m => m.IndexingStatus == "completed"),
            FailedIndexes = mappings.Count(m => m.IndexingStatus == "failed"),
            RunningTasks = runningTasks.Count,
            TotalSnippets = mappings.Sum(m => m.Statistics.IndexedSnippets),
            TotalFiles = mappings.Sum(m => m.Statistics.TotalFiles),
            MonitoredCodebases = mappings.Count(m => m.IsMonitoring),
            QdrantConnection = connectionStats,
            TaskPersistence = storageStats,
            LastUpdated = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 强制清理所有已完成的任务
    /// </summary>
    public async Task<object> CleanupCompletedTasksAsync()
    {
        var cleanedCount = await _persistenceService.CleanupCompletedTasksAsync();
        _logger.LogInformation("手动清理了 {Count} 个已完成任务", cleanedCount);
        
        return new
        {
            CleanedTasksCount = cleanedCount,
            CleanupTime = DateTime.UtcNow,
            Message = $"已清理 {cleanedCount} 个已完成任务"
        };
    }

    /// <summary>
    /// 获取Qdrant连接状态
    /// </summary>
    public async Task<object> GetConnectionStatusAsync()
    {
        return await _connectionMonitor.GetConnectionStatisticsAsync();
    }

    /// <summary>
    /// 强制检查Qdrant连接
    /// </summary>
    public async Task<object> ForceCheckConnectionAsync()
    {
        var isConnected = await _connectionMonitor.ForceCheckAsync();
        return new
        {
            IsConnected = isConnected,
            CheckTime = DateTime.UtcNow,
            Message = isConnected ? "连接正常" : "连接失败"
        };
    }
}