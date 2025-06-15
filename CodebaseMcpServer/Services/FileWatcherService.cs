using System.Collections.Concurrent;
using CodebaseMcpServer.Extensions;
using Models = CodebaseMcpServer.Models; // Namespace alias

namespace CodebaseMcpServer.Services;

/// <summary>
/// 文件监控服务 - 监控代码库文件变更并自动更新索引
/// </summary>
public class FileWatcherService : BackgroundService
{
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, List<Models.FileChangeEvent>> _pendingChanges = new(); // Explicitly use Models.FileChangeEvent
    private readonly Timer? _batchProcessor;
    private readonly ILogger<FileWatcherService> _logger;
    private readonly IndexConfigManager _configManager;
    private readonly IServiceProvider _serviceProvider;  // 用于延迟获取 IndexingTaskManager
    private readonly IConfiguration _configuration;
    private readonly EnhancedCodeSemanticSearch _searchService;
    private IndexingTaskManager? _taskManager; // 延迟初始化
    private readonly FileChangePersistenceService _fileChangePersistence; // 新增持久化服务依赖

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IndexConfigManager configManager,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        EnhancedCodeSemanticSearch searchService,
        FileChangePersistenceService fileChangePersistence) // 注入新的服务
    {
        _logger = logger;
        _configManager = configManager;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _searchService = searchService;
        _fileChangePersistence = fileChangePersistence; // 初始化新的服务
        
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
            var enableRecovery = _configuration.GetValue<bool>("FileWatcher:EnableRecovery", true);
            if (enableRecovery)
            {
                // 🔥 关键改进：启动时先恢复未完成的变更
                await RecoverPendingChanges(stoppingToken);
            }
            
            // 初始化已配置的监控
            await InitializeWatchers();

            var enablePeriodicCleanup = _configuration.GetValue<bool>("FileChangePersistence:EnablePeriodicCleanup", true);
            if (enablePeriodicCleanup)
            {
                 // 启动定期清理任务
                _ = Task.Run(() => StartPeriodicCleanup(stoppingToken), stoppingToken);
            }
            
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

    private async Task RecoverPendingChanges(CancellationToken stoppingToken)
    {
        if (stoppingToken.IsCancellationRequested) return;

        try
        {
            _logger.LogInformation("开始恢复未完成的文件变更...");
            // 加载待处理和处理中的变更
            var pendingChanges = await _fileChangePersistence.LoadPendingChangesAsync();
            var processingChanges = await _fileChangePersistence.LoadProcessingChangesAsync();
            
            var changesToReprocess = new List<Models.FileChangeEvent>();

            // 将正在处理的变更重置为待处理状态
            foreach (var change in processingChanges)
            {
                if (stoppingToken.IsCancellationRequested) return;
                change.Status = Models.FileChangeStatus.Pending;
                change.ErrorMessage = "服务重启，重新排队处理";
                change.RetryCount = 0; // 重置重试次数
                await _fileChangePersistence.UpdateChangeAsync(change);
                changesToReprocess.Add(change);
                _logger.LogInformation("将处理中的变更 {Id} 重置为待处理: {Path}", change.Id, (object)change.FilePath);
            }
            
            changesToReprocess.AddRange(pendingChanges);
            
            var totalRecovered = changesToReprocess.Count;
            if (totalRecovered > 0)
            {
                _logger.LogInformation("服务启动时恢复了 {Count} 个未完成的文件变更进行处理。", totalRecovered);
                
                // 立即触发一次处理
                // 按集合分组并去重处理
                var groupedChanges = changesToReprocess
                    .GroupBy(c => c.CollectionName)
                    .ToDictionary(g => g.Key, g => DeduplicateChanges(g.ToList()));
            
                foreach (var kvp in groupedChanges)
                {
                    if (stoppingToken.IsCancellationRequested) return;
                    _logger.LogInformation("恢复处理持久化变更: 集合 {Collection}, 变更数 {Count}",
                        kvp.Key, kvp.Value.Count);
                    await ProcessCollectionChanges(kvp.Key, kvp.Value);
                }
                _logger.LogInformation("所有恢复的文件变更已提交处理。");
            }
            else
            {
                _logger.LogInformation("没有发现需要恢复的文件变更。");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复待处理文件变更失败");
        }
    }

    private async Task StartPeriodicCleanup(CancellationToken cancellationToken)
    {
        var cleanupIntervalHours = _configuration.GetValue<int>("FileChangePersistence:CleanupIntervalHours", 24);
        var maxAgeHours = _configuration.GetValue<int>("FileChangePersistence:MaxAgeHours", 168); // 7 days
        
        if(cleanupIntervalHours <=0 || maxAgeHours <=0)
        {
            _logger.LogWarning("定期清理任务因配置无效而禁用 (CleanupIntervalHours: {CleanupInterval}, MaxAgeHours: {MaxAge})", cleanupIntervalHours, maxAgeHours);
            return;
        }

        var maxAge = TimeSpan.FromHours(maxAgeHours);
        _logger.LogInformation("文件变更记录定期清理任务已启动。清理间隔: {Hours}小时, 最大保留时间: {MaxAgeHours}小时。", cleanupIntervalHours, maxAgeHours);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(cleanupIntervalHours), cancellationToken);
                
                if (cancellationToken.IsCancellationRequested) break;

                _logger.LogInformation("开始执行文件变更记录定期清理...");
                var cleanedCount = await _fileChangePersistence.CleanupExpiredChangesAsync(maxAge);
                if (cleanedCount > 0)
                {
                    _logger.LogInformation("定期清理了 {Count} 个过期的文件变更记录", cleanedCount);
                }
                else
                {
                    _logger.LogInformation("定期清理未发现过期记录。");
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("文件变更记录定期清理任务已取消。");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "文件变更记录定期清理任务失败");
            }
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
    public async Task<bool> CreateWatcher(Models.CodebaseMapping mapping) // Use Models alias
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
            watcher.Created += (s, e) => OnFileChanged(mapping, e, Models.FileChangeType.Created);
            watcher.Changed += (s, e) => OnFileChanged(mapping, e, Models.FileChangeType.Modified);
            watcher.Deleted += (s, e) => OnFileChanged(mapping, e, Models.FileChangeType.Deleted);
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
    private void OnFileChanged(Models.CodebaseMapping mapping, FileSystemEventArgs e, Models.FileChangeType changeType) // Use Models alias
    {
        try
        {
            // 检查文件扩展名
            if (!e.FullPath.IsSupportedExtension(mapping.WatcherConfig.FileExtensions))
                return;

            // 检查是否在排除目录中
            if (e.FullPath.IsExcludedPath(mapping.WatcherConfig.ExcludeDirectories))
                return;

            var logFileChanges = _configuration.GetValue<bool>("FileWatcher:LogFileChanges", true);
            if (logFileChanges)
            {
                _logger.LogDebug("检测到文件变更: {Type} {Path}", changeType, e.FullPath);
            }

            // 🔥 核心改进：创建变更事件并立即持久化
            var changeEvent = new Models.FileChangeEvent // Explicitly use Models.FileChangeEvent
            {
                FilePath = e.FullPath,
                ChangeType = changeType,
                Timestamp = DateTime.UtcNow,
                CollectionName = mapping.CollectionName,
                Status = Models.FileChangeStatus.Pending // Explicitly use Models.FileChangeStatus
            };

            // 异步持久化，不阻塞文件监控
            _ = Task.Run(async () => await PersistFileChange(changeEvent));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理文件变更事件失败: {Path}", e.FullPath);
        }
    }

    private async Task PersistFileChange(Models.FileChangeEvent changeEvent) // Explicitly use Models.FileChangeEvent
    {
        try
        {
            var enablePersistence = _configuration.GetValue<bool>("FileChangePersistence:EnablePersistence", true);
            if (!enablePersistence)
            {
                // 持久化被禁用，回退到内存队列 (旧逻辑)
                lock (_pendingChanges)
                {
                    if (!_pendingChanges.ContainsKey(changeEvent.CollectionName))
                    {
                        _pendingChanges[changeEvent.CollectionName] = new List<Models.FileChangeEvent>(); // Explicitly use Models.FileChangeEvent
                    }
                    _pendingChanges[changeEvent.CollectionName].Add(changeEvent);
                    _logger.LogDebug("文件变更持久化已禁用，添加到内存队列: {Id} - {Path}", changeEvent.Id, (object)changeEvent.FilePath);
                }
                return;
            }

            await _fileChangePersistence.SaveChangeAsync(changeEvent);
            _logger.LogDebug("文件变更已持久化: {Id} - {Path}", changeEvent.Id, (object)changeEvent.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "持久化文件变更失败: {Path}", (object)changeEvent.FilePath);
            // 降级策略：持久化失败时添加到内存队列
            lock (_pendingChanges)
            {
                if (!_pendingChanges.ContainsKey(changeEvent.CollectionName))
                {
                    _pendingChanges[changeEvent.CollectionName] = new List<Models.FileChangeEvent>(); // Explicitly use Models.FileChangeEvent
                }
                _pendingChanges[changeEvent.CollectionName].Add(changeEvent);
                _logger.LogWarning("持久化文件变更失败，已添加到内存队列: {Id} - {Path}", changeEvent.Id, (object)changeEvent.FilePath);
            }
        }
    }

    /// <summary>
    /// 处理文件重命名事件
    /// </summary>
    private void OnFileRenamed(Models.CodebaseMapping mapping, RenamedEventArgs e) // Use Models alias
    {
        try
        {
            // 先处理删除旧文件
            OnFileChanged(mapping, new FileSystemEventArgs(WatcherChangeTypes.Deleted,
                Path.GetDirectoryName(e.OldFullPath) ?? "", e.OldName ?? ""), Models.FileChangeType.Deleted); // 使用 Models.FileChangeType

            // 再处理创建新文件
            OnFileChanged(mapping, e, Models.FileChangeType.Created); // 使用 Models.FileChangeType

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
    private void OnWatcherError(Models.CodebaseMapping mapping, ErrorEventArgs e) // Use Models alias
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
        // 🔥 改进：从持久化存储加载待处理的变更 (如果启用)
        // 仍然保留内存队列的处理，用于持久化禁用或失败时的降级
        ProcessInMemoryChanges();
        _ = Task.Run(async () => await ProcessPersistedChanges());
    }

    private void ProcessInMemoryChanges()
    {
        Dictionary<string, List<Models.FileChangeEvent>> changesToProcessMemory; // Explicitly use Models.FileChangeEvent
        lock (_pendingChanges)
        {
            if (_pendingChanges.Count == 0)
                return;
            changesToProcessMemory = new Dictionary<string, List<Models.FileChangeEvent>>(_pendingChanges); // Explicitly use Models.FileChangeEvent
            _pendingChanges.Clear();
        }

        if (changesToProcessMemory.Any())
        {
             _logger.LogInformation("开始处理内存队列中的 {Count} 个集合的变更", changesToProcessMemory.Count);
            foreach (var kvp in changesToProcessMemory)
            {
                var collectionName = kvp.Key;
                var changes = DeduplicateChanges(kvp.Value); // 去重
                 _logger.LogInformation("处理内存队列变更: 集合 {Collection}, 变更数 {Count}",
                    collectionName, changes.Count);
                _ = Task.Run(async () => await ProcessCollectionChanges(collectionName, changes));
            }
        }
    }
    
    private async Task ProcessPersistedChanges()
    {
        var enablePersistence = _configuration.GetValue<bool>("FileChangePersistence:EnablePersistence", true);
        if (!enablePersistence) return;

        try
        {
            var pendingChanges = await _fileChangePersistence.LoadPendingChangesAsync();
            
            if (pendingChanges.Count == 0)
                return;

            _logger.LogInformation("发现 {Count} 个待处理的持久化文件变更", pendingChanges.Count);

            // 按集合分组并去重处理
            var groupedChanges = pendingChanges
                .GroupBy(c => c.CollectionName)
                .ToDictionary(g => g.Key, g => DeduplicateChanges(g.ToList()));
            
            foreach (var kvp in groupedChanges)
            {
                _logger.LogInformation("处理持久化变更: 集合 {Collection}, 变更数 {Count}",
                    kvp.Key, kvp.Value.Count);
                await ProcessCollectionChanges(kvp.Key, kvp.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理持久化文件变更失败");
        }
    }

    private List<Models.FileChangeEvent> DeduplicateChanges(List<Models.FileChangeEvent> changes) // Explicitly use Models.FileChangeEvent
    {
        // 同一文件的多次变更只保留最新的
        return changes
            .GroupBy(c => c.FilePath)
            .Select(g => g.OrderByDescending(c => c.Timestamp).First())
            .ToList();
    }

    /// <summary>
    /// 处理特定集合的文件变更
    /// </summary>
    private async Task ProcessCollectionChanges(string collectionName, List<Models.FileChangeEvent> changes) // Explicitly use Models.FileChangeEvent
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
                    _logger.LogError(ex, "处理文件变更失败: {FilePath}", (object)change.FilePath); // Cast to object for logging
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
    private async Task<bool> ProcessSingleFileChange(Models.FileChangeEvent change)
    {
        var enablePersistence = _configuration.GetValue<bool>("FileChangePersistence:EnablePersistence", true);

        try
        {
            if (enablePersistence)
            {
                change.Status = Models.FileChangeStatus.Processing;
                await _fileChangePersistence.UpdateChangeAsync(change);
            }

            bool success = false;
            string errorMessage = string.Empty;

            switch (change.ChangeType)
            {
                case Models.FileChangeType.Created:
                case Models.FileChangeType.Modified:
                    if (File.Exists(change.FilePath))
                    {
                        var taskManager = GetTaskManager();
                        success = await taskManager.UpdateFileIndexAsync(change.FilePath, change.CollectionName);
                        if (!success) errorMessage = "文件索引更新失败";
                    }
                    else
                    {
                        _logger.LogWarning("尝试处理的文件不存在，可能已被删除或重命名: {Path}", (object)change.FilePath); // Cast to object
                        errorMessage = "文件不存在，跳过处理";
                        success = true;
                    }
                    break;

                case Models.FileChangeType.Deleted:
                    _logger.LogInformation("文件已删除，开始清理索引: {Path}", (object)change.FilePath); // Cast to object
                    var taskManagerForDelete = GetTaskManager();
                    success = await taskManagerForDelete.HandleFileDeletionAsync(change.FilePath, change.CollectionName);
                    if (!success) errorMessage = "删除文件索引失败";
                    break;
            
                case Models.FileChangeType.Renamed:
                    success = true;
                    break;
            }

            if (enablePersistence)
            {
                if (success)
                {
                    change.Status = Models.FileChangeStatus.Completed;
                    change.ProcessedAt = DateTime.UtcNow;
                    await _fileChangePersistence.UpdateChangeAsync(change);
                    await _fileChangePersistence.CleanupChangeAsync(change.Id);
                    _logger.LogDebug("文件变更处理完成并清理持久化记录: {Id} - {Path}", change.Id, (object)change.FilePath); // Cast to object
                }
                else
                {
                    change.Status = Models.FileChangeStatus.Failed;
                    change.ErrorMessage = errorMessage;
                    change.RetryCount++;
                    change.LastRetryAt = DateTime.UtcNow;
                    await _fileChangePersistence.UpdateChangeAsync(change);
                    _logger.LogWarning("文件变更处理失败，已更新持久化记录: {Id} - {Path} - {Error}",
                        change.Id, (object)change.FilePath, errorMessage); // Cast to object
                }
            }
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理单个文件变更时发生异常: {FilePath}", (object)change.FilePath); // Cast to object
            if (enablePersistence)
            {
                change.Status = Models.FileChangeStatus.Failed;
                change.ErrorMessage = ex.Message;
                change.RetryCount++;
                change.LastRetryAt = DateTime.UtcNow;
                await _fileChangePersistence.UpdateChangeAsync(change);
                _logger.LogError(ex, "文件变更处理异常，已更新持久化记录: {Id} - {Path}", change.Id, (object)change.FilePath); // Cast to object
            }
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