using System.Text.Json;
using CodebaseMcpServer.Models;
using CodebaseMcpServer.Extensions;

namespace CodebaseMcpServer.Services;

/// <summary>
/// 任务持久化服务 - 管理索引任务的本地存储和恢复
/// </summary>
public class TaskPersistenceService
{
    private readonly string _taskStorePath;
    private readonly ILogger<TaskPersistenceService> _logger;
    private readonly object _fileLock = new object();

    public TaskPersistenceService(ILogger<TaskPersistenceService> logger, IConfiguration configuration)
    {
        _logger = logger;
        var baseDir = configuration.GetValue<string>("TaskPersistence:StorageDirectory") ?? "task-storage";
        _taskStorePath = Path.Combine(Directory.GetCurrentDirectory(), baseDir);
        
        // 确保目录存在
        Directory.CreateDirectory(_taskStorePath);
        _logger.LogInformation("任务持久化存储目录: {Path}", _taskStorePath);
    }

    /// <summary>
    /// 保存任务到本地存储
    /// </summary>
    public async Task<bool> SaveTaskAsync(IndexingTask task)
    {
        try
        {
            var taskFile = Path.Combine(_taskStorePath, $"{task.Id}.json");
            var taskData = new PersistedTask
            {
                Task = task,
                SavedAt = DateTime.UtcNow,
                Version = "1.0"
            };

            var json = JsonSerializer.Serialize(taskData, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            lock (_fileLock)
            {
                File.WriteAllText(taskFile, json);
            }

            _logger.LogDebug("任务已保存到本地存储: {TaskId} -> {FilePath}", task.Id, taskFile);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存任务失败: {TaskId}", task.Id);
            return false;
        }
    }

    /// <summary>
    /// 更新任务状态
    /// </summary>
    public async Task<bool> UpdateTaskAsync(IndexingTask task)
    {
        return await SaveTaskAsync(task); // 直接覆盖保存
    }

    /// <summary>
    /// 从本地存储加载任务
    /// </summary>
    public async Task<IndexingTask?> LoadTaskAsync(string taskId)
    {
        try
        {
            var taskFile = Path.Combine(_taskStorePath, $"{taskId}.json");
            if (!File.Exists(taskFile))
            {
                return null;
            }

            string json;
            lock (_fileLock)
            {
                json = File.ReadAllText(taskFile);
            }

            var taskData = JsonSerializer.Deserialize<PersistedTask>(json, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            _logger.LogDebug("任务已从本地存储加载: {TaskId}", taskId);
            return taskData?.Task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载任务失败: {TaskId}", taskId);
            return null;
        }
    }

    /// <summary>
    /// 获取所有未完成的任务
    /// </summary>
    public async Task<List<IndexingTask>> LoadPendingTasksAsync()
    {
        var pendingTasks = new List<IndexingTask>();

        try
        {
            var taskFiles = Directory.GetFiles(_taskStorePath, "*.json");
            
            foreach (var taskFile in taskFiles)
            {
                try
                {
                    string json;
                    lock (_fileLock)
                    {
                        json = File.ReadAllText(taskFile);
                    }

                    var taskData = JsonSerializer.Deserialize<PersistedTask>(json, new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    if (taskData?.Task != null && 
                        (taskData.Task.Status == IndexingStatus.Pending || 
                         taskData.Task.Status == IndexingStatus.Running))
                    {
                        // 重启后将运行中的任务标记为待处理
                        if (taskData.Task.Status == IndexingStatus.Running)
                        {
                            taskData.Task.Status = IndexingStatus.Pending;
                            taskData.Task.ErrorMessage = "服务重启，任务重新排队";
                        }

                        pendingTasks.Add(taskData.Task);
                        _logger.LogInformation("发现未完成任务: {TaskId} - {Status}", taskData.Task.Id, taskData.Task.Status);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "跳过无效的任务文件: {FilePath}", taskFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载待处理任务失败");
        }

        _logger.LogInformation("加载了 {Count} 个待处理任务", pendingTasks.Count);
        return pendingTasks;
    }

    /// <summary>
    /// 删除已完成的任务
    /// </summary>
    public async Task<bool> CleanupTaskAsync(string taskId)
    {
        try
        {
            var taskFile = Path.Combine(_taskStorePath, $"{taskId}.json");
            if (File.Exists(taskFile))
            {
                lock (_fileLock)
                {
                    File.Delete(taskFile);
                }
                _logger.LogDebug("任务文件已清理: {TaskId}", taskId);
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理任务文件失败: {TaskId}", taskId);
            return false;
        }
    }

    /// <summary>
    /// 清理所有已完成的任务
    /// </summary>
    public async Task<int> CleanupCompletedTasksAsync()
    {
        int cleanedCount = 0;

        try
        {
            var taskFiles = Directory.GetFiles(_taskStorePath, "*.json");
            
            foreach (var taskFile in taskFiles)
            {
                try
                {
                    string json;
                    lock (_fileLock)
                    {
                        json = File.ReadAllText(taskFile);
                    }

                    var taskData = JsonSerializer.Deserialize<PersistedTask>(json, new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    if (taskData?.Task != null && 
                        (taskData.Task.Status == IndexingStatus.Completed || 
                         taskData.Task.Status == IndexingStatus.Failed ||
                         taskData.Task.Status == IndexingStatus.Cancelled))
                    {
                        lock (_fileLock)
                        {
                            File.Delete(taskFile);
                        }
                        cleanedCount++;
                        _logger.LogDebug("清理已完成任务: {TaskId} - {Status}", taskData.Task.Id, taskData.Task.Status);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "清理任务文件时出错: {FilePath}", taskFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量清理任务失败");
        }

        _logger.LogInformation("清理了 {Count} 个已完成任务", cleanedCount);
        return cleanedCount;
    }

    /// <summary>
    /// 获取存储统计信息
    /// </summary>
    public async Task<object> GetStorageStatisticsAsync()
    {
        try
        {
            var taskFiles = Directory.GetFiles(_taskStorePath, "*.json");
            var taskCounts = new Dictionary<string, int>
            {
                ["pending"] = 0,
                ["running"] = 0,
                ["completed"] = 0,
                ["failed"] = 0,
                ["cancelled"] = 0
            };

            foreach (var taskFile in taskFiles)
            {
                try
                {
                    string json;
                    lock (_fileLock)
                    {
                        json = File.ReadAllText(taskFile);
                    }

                    var taskData = JsonSerializer.Deserialize<PersistedTask>(json, new JsonSerializerOptions 
                    { 
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });

                    if (taskData?.Task != null)
                    {
                        var status = taskData.Task.Status.ToString().ToLower();
                        if (taskCounts.ContainsKey(status))
                        {
                            taskCounts[status]++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "统计任务文件时出错: {FilePath}", taskFile);
                }
            }

            return new
            {
                StoragePath = _taskStorePath,
                TotalTasks = taskFiles.Length,
                TasksByStatus = taskCounts,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取存储统计失败");
            return new
            {
                StoragePath = _taskStorePath,
                Error = ex.Message,
                LastUpdated = DateTime.UtcNow
            };
        }
    }
}

/// <summary>
/// 持久化任务数据模型
/// </summary>
public class PersistedTask
{
    public IndexingTask Task { get; set; } = new();
    public DateTime SavedAt { get; set; }
    public string Version { get; set; } = "1.0";
}