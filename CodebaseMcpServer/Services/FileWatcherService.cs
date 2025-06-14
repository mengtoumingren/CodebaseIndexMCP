using System.Collections.Concurrent;
using CodebaseMcpServer.Models;
using CodebaseMcpServer.Extensions;

namespace CodebaseMcpServer.Services;

/// <summary>
/// 文件监控服务 - 监控代码库文件变更并自动更新索引
/// </summary>
public class FileWatcherService : BackgroundService
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, List<FileChangeEvent>> _pendingChanges = new();
    private readonly Timer? _batchProcessor;
    private readonly ILogger<FileWatcherService> _logger;
    private readonly IndexConfigManager _configManager;
    private readonly IServiceProvider _serviceProvider;  // 用于延迟获取 IndexingTaskManager
    private readonly IConfiguration _configuration;
    private readonly EnhancedCodeSemanticSearch _searchService;
    private IndexingTaskManager? _taskManager; // 延迟初始化

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IndexConfigManager configManager,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        EnhancedCodeSemanticSearch searchService)
    {
        _logger = logger;
        _configManager = configManager;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _searchService = searchService;
        
        _logger.LogDebug("FileWatcherService 构造函数开始执行");
        
        // 启动批处理定时器
        var batchDelay = _configuration.GetValue<int>("FileWatcher:BatchProcessingDelay", 5000);
        _batchProcessor = new Timer(ProcessPendingChanges, null,
            TimeSpan.Zero, TimeSpan.FromMilliseconds(batchDelay));
            
        _logger.LogDebug("FileWatcherService 构造函数执行完成");
    }

    /// <summary>
    /// 延迟获取 IndexingTaskManager 以避免循环依赖
    /// </summary>
    private IndexingTaskManager GetTaskManager()
    {
        if (_taskManager == null)
        {
            _taskManager = _serviceProvider.GetRequiredService<IndexingTaskManager>();
            _logger.LogDebug("延迟获取 IndexingTaskManager 成功");
        }
        return _taskManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("文件监控服务启动");
        
        try
        {
            // 初始化已配置的监控
            await InitializeWatchers();
            
            // 等待取消信号
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("文件监控服务正在停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "文件监控服务运行时发生错误");
        }
        finally
        {
            // 清理资源
            DisposeWatchers();
        }
    }

    /// <summary>
    /// 初始化文件监控器
    /// </summary>
    private async Task InitializeWatchers()
    {
        try
        {
            var config = await _configManager.GetConfiguration();
            var enableAutoMonitoring = _configuration.GetValue<bool>("FileWatcher:EnableAutoMonitoring", true);
            
            if (!enableAutoMonitoring)
            {
                _logger.LogInformation("自动文件监控已禁用");
                return;
            }

            var monitoredMappings = config.CodebaseMappings
                .Where(m => m.IsMonitoring && m.IndexingStatus == "completed")
                .ToList();

            _logger.LogInformation("初始化文件监控，监控 {Count} 个代码库", monitoredMappings.Count);

            foreach (var mapping in monitoredMappings)
            {
                await CreateWatcher(mapping);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化文件监控失败");
        }
    }

    /// <summary>
    /// 为指定代码库创建文件监控器
    /// </summary>
    public async Task<bool> CreateWatcher(CodebaseMapping mapping)
    {
        try
        {
            if (!Directory.Exists(mapping.CodebasePath))
            {
                _logger.LogWarning("监控目录不存在，跳过: {Path}", mapping.CodebasePath);
                return false;
            }

            if (_watchers.ContainsKey(mapping.NormalizedPath))
            {
                _logger.LogDebug("监控器已存在: {Path}", mapping.CodebasePath);
                return true;
            }

            var watcher = new FileSystemWatcher(mapping.CodebasePath)
            {
                Filter = "*.cs",
                IncludeSubdirectories = mapping.WatcherConfig.IncludeSubdirectories,
                EnableRaisingEvents = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
            };

            // 绑定事件处理器
            watcher.Created += (s, e) => OnFileChanged(mapping, e, FileChangeType.Created);
            watcher.Changed += (s, e) => OnFileChanged(mapping, e, FileChangeType.Modified);
            watcher.Deleted += (s, e) => OnFileChanged(mapping, e, FileChangeType.Deleted);
            watcher.Renamed += (s, e) => OnFileRenamed(mapping, e);

            // 错误处理
            watcher.Error += (s, e) => OnWatcherError(mapping, e);

            _watchers[mapping.NormalizedPath] = watcher;

            _logger.LogInformation("开始监控代码库: {FriendlyName} -> {CollectionName} (路径: {Path})", 
                mapping.FriendlyName, mapping.CollectionName, mapping.CodebasePath);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建文件监控器失败: {Path}", mapping.CodebasePath);
            return false;
        }
    }

    /// <summary>
    /// 停止监控指定代码库
    /// </summary>
    public bool StopWatcher(string codebasePath)
    {
        var normalizedPath = codebasePath.NormalizePath();
        
        if (_watchers.TryGetValue(normalizedPath, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _watchers.Remove(normalizedPath);
            
            _logger.LogInformation("停止监控代码库: {Path}", codebasePath);
            return true;
        }
        
        return false;
    }

    /// <summary>
    /// 处理文件变更事件
    /// </summary>
    private void OnFileChanged(CodebaseMapping mapping, FileSystemEventArgs e, FileChangeType changeType)
    {
        try
        {
            // 检查文件扩展名
            if (!e.FullPath.IsSupportedExtension(mapping.WatcherConfig.FileExtensions))
                return;

            // 检查是否在排除目录中
            if (e.FullPath.IsExcludedPath(mapping.WatcherConfig.ExcludeDirectories))
                return;

            // 记录变更事件
            var logFileChanges = _configuration.GetValue<bool>("FileWatcher:LogFileChanges", true);
            if (logFileChanges)
            {
                _logger.LogDebug("检测到文件变更: {Type} {Path}", changeType, e.FullPath);
            }

            // 添加到待处理队列
            lock (_pendingChanges)
            {
                if (!_pendingChanges.ContainsKey(mapping.CollectionName))
                {
                    _pendingChanges[mapping.CollectionName] = new List<FileChangeEvent>();
                }

                _pendingChanges[mapping.CollectionName].Add(new FileChangeEvent
                {
                    FilePath = e.FullPath,
                    ChangeType = changeType,
                    Timestamp = DateTime.UtcNow,
                    CollectionName = mapping.CollectionName
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理文件变更事件失败: {Path}", e.FullPath);
        }
    }

    /// <summary>
    /// 处理文件重命名事件
    /// </summary>
    private void OnFileRenamed(CodebaseMapping mapping, RenamedEventArgs e)
    {
        try
        {
            // 先处理删除旧文件
            OnFileChanged(mapping, new FileSystemEventArgs(WatcherChangeTypes.Deleted, 
                Path.GetDirectoryName(e.OldFullPath) ?? "", e.OldName ?? ""), FileChangeType.Deleted);

            // 再处理创建新文件
            OnFileChanged(mapping, e, FileChangeType.Created);

            _logger.LogDebug("文件重命名: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理文件重命名事件失败: {OldPath} -> {NewPath}", e.OldFullPath, e.FullPath);
        }
    }

    /// <summary>
    /// 处理监控器错误
    /// </summary>
    private void OnWatcherError(CodebaseMapping mapping, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "文件监控器发生错误: {Path}", mapping.CodebasePath);
        
        // 尝试重新创建监控器
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000); // 等待5秒后重试
            
            try
            {
                StopWatcher(mapping.CodebasePath);
                await CreateWatcher(mapping);
                _logger.LogInformation("文件监控器已重新创建: {Path}", mapping.CodebasePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "重新创建文件监控器失败: {Path}", mapping.CodebasePath);
            }
        });
    }

    /// <summary>
    /// 批处理待处理的文件变更
    /// </summary>
    private void ProcessPendingChanges(object? state)
    {
        Dictionary<string, List<FileChangeEvent>> changesToProcess;

        lock (_pendingChanges)
        {
            if (_pendingChanges.Count == 0)
                return;

            changesToProcess = new Dictionary<string, List<FileChangeEvent>>(_pendingChanges);
            _pendingChanges.Clear();
        }

        foreach (var kvp in changesToProcess)
        {
            var collectionName = kvp.Key;
            var changes = kvp.Value;

            // 去重处理：同一文件的多次变更只保留最新的
            var deduplicatedChanges = changes
                .GroupBy(c => c.FilePath)
                .Select(g => g.OrderByDescending(c => c.Timestamp).First())
                .ToList();

            if (deduplicatedChanges.Count < changes.Count)
            {
                _logger.LogDebug("去重文件变更: {Original} -> {Deduplicated} (集合: {Collection})", 
                    changes.Count, deduplicatedChanges.Count, collectionName);
            }

            _logger.LogInformation("批处理文件变更: 集合 {Collection}, 变更数 {Count}", 
                collectionName, deduplicatedChanges.Count);

            // 异步处理变更
            _ = Task.Run(async () => await ProcessCollectionChanges(collectionName, deduplicatedChanges));
        }
    }

    /// <summary>
    /// 处理特定集合的文件变更
    /// </summary>
    private async Task ProcessCollectionChanges(string collectionName, List<FileChangeEvent> changes)
    {
        try
        {
            var processedCount = 0;
            var errorCount = 0;

            foreach (var change in changes)
            {
                try
                {
                    var success = await ProcessSingleFileChange(change);
                    if (success)
                    {
                        processedCount++;
                    }
                    else
                    {
                        errorCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "处理文件变更失败: {FilePath}", change.FilePath);
                    errorCount++;
                }
            }

            _logger.LogInformation("集合 {Collection} 文件变更处理完成: 成功 {Success}, 失败 {Error}", 
                collectionName, processedCount, errorCount);

            // 更新统计信息
            await UpdateMappingStatistics(collectionName, processedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理集合文件变更失败: {Collection}", collectionName);
        }
    }

    /// <summary>
    /// 处理单个文件变更
    /// </summary>
    private async Task<bool> ProcessSingleFileChange(FileChangeEvent change)
    {
        try
        {
            switch (change.ChangeType)
            {
                case FileChangeType.Created:
                case FileChangeType.Modified:
                    if (File.Exists(change.FilePath))
                    {
                        var taskManager = GetTaskManager();
                        return await taskManager.UpdateFileIndexAsync(change.FilePath, change.CollectionName);
                    }
                    break;

                case FileChangeType.Deleted:
                    // TODO: 实现删除文件的索引清理功能
                    _logger.LogInformation("文件已删除，需要清理索引: {Path}", change.FilePath);
                    return true; // 暂时返回成功

                case FileChangeType.Renamed:
                    // 重命名通过删除+创建两个事件处理
                    break;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理单个文件变更失败: {FilePath}", change.FilePath);
            return false;
        }
    }

    /// <summary>
    /// 更新映射统计信息
    /// </summary>
    private async Task UpdateMappingStatistics(string collectionName, int updatedCount)
    {
        try
        {
            var mapping = _configManager.GetMappingByCollection(collectionName);
            if (mapping != null)
            {
                await _configManager.UpdateMappingStatistics(mapping.Id, stats =>
                {
                    stats.LastUpdateTime = DateTime.UtcNow;
                    // 可以在这里添加更多统计信息的更新
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新映射统计信息失败: {Collection}", collectionName);
        }
    }

    /// <summary>
    /// 获取监控状态
    /// </summary>
    public object GetMonitoringStatus()
    {
        return new
        {
            ActiveWatchers = _watchers.Count,
            MonitoredPaths = _watchers.Keys.ToList(),
            PendingChanges = _pendingChanges.Values.Sum(list => list.Count),
            IsRunning = !_disposed
        };
    }

    /// <summary>
    /// 清理监控器资源
    /// </summary>
    private void DisposeWatchers()
    {
        foreach (var watcher in _watchers.Values)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理文件监控器时发生错误");
            }
        }
        
        _watchers.Clear();
        _logger.LogInformation("所有文件监控器已清理");
    }

    private bool _disposed = false;

    public override void Dispose()
    {
        if (!_disposed)
        {
            _batchProcessor?.Dispose();
            DisposeWatchers();
            _disposed = true;
        }
        
        base.Dispose();
    }
}