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
    private readonly IServiceProvider _serviceProvider;  // 用于延迟获取 FileWatcherService
    private FileWatcherService? _fileWatcherService; // 延迟初始化

    public IndexingTaskManager(
        ILogger<IndexingTaskManager> logger,
        IndexConfigManager configManager,
        EnhancedCodeSemanticSearch searchService,
        IConfiguration configuration,
        TaskPersistenceService persistenceService,
        QdrantConnectionMonitor connectionMonitor,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configManager = configManager;
        _searchService = searchService;
        _configuration = configuration;
        _persistenceService = persistenceService;
        _connectionMonitor = connectionMonitor;
        _serviceProvider = serviceProvider;
        
        _logger.LogDebug("IndexingTaskManager 构造函数开始执行");
        
        // 启动时恢复未完成的任务
        _ = Task.Run(RestorePendingTasksAsync);
        
        _logger.LogDebug("IndexingTaskManager 构造函数执行完成");
    }

    /// <summary>
    /// 延迟获取 FileWatcherService 以避免循环依赖
    /// </summary>
    private FileWatcherService GetFileWatcherService()
    {
        if (_fileWatcherService == null)
        {
            _fileWatcherService = _serviceProvider.GetRequiredService<FileWatcherService>();
            _logger.LogDebug("延迟获取 FileWatcherService 成功");
        }
        return _fileWatcherService;
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
    /// 获取并发设置
    /// </summary>
    private ConcurrencySettings GetConcurrencySettings()
    {
        var settings = new ConcurrencySettings();
        
        // 从配置中读取并发设置
        var concurrencySection = _configuration.GetSection("CodeSearch:IndexingSettings:concurrencySettings");
        if (concurrencySection.Exists())
        {
            concurrencySection.Bind(settings);
        }
        
        // 根据硬件环境优化配置
        settings.OptimizeForEnvironment();
        
        _logger.LogDebug("并发设置: MaxConcurrentEmbeddingRequests={MaxEmbedding}, MaxConcurrentFileBatches={MaxFile}",
            settings.MaxConcurrentEmbeddingRequests, settings.MaxConcurrentFileBatches);
        
        return settings;
    }

    /// <summary>
    /// 按批次并发处理代码库并建立索引 - 增强版
    /// </summary>
    public async Task<int> ProcessCodebaseInBatchesConcurrentlyAsync(
        string codebasePath,
        string collectionName,
        List<string>? filePatterns = null,
        int batchSize = 10,
        ConcurrencySettings? concurrencySettings = null,
        Func<int, int, string, Task>? progressCallback = null)
    {
        filePatterns ??= new List<string> { "*.cs" };
        var settings = concurrencySettings ?? GetConcurrencySettings();
        
        // 确保集合存在
        if (!await _searchService.EnsureCollectionAsync(collectionName))
        {
            throw new InvalidOperationException($"无法创建或访问集合: {collectionName}");
        }
        
        // 获取所有匹配的文件
        var allFiles = GetMatchingFiles(codebasePath, filePatterns);
        var totalFiles = allFiles.Count;
        var totalSnippets = 0;
        var processedFiles = 0;
        
        _logger.LogInformation("开始并发批处理索引：{TotalFiles} 个文件，批大小：{BatchSize}，并发度：{Concurrency}",
            totalFiles, batchSize, settings.MaxConcurrentFileBatches);
        
        // 创建文件批次
        var fileBatches = CreateFileBatches(allFiles, batchSize);
        var concurrencyLimiter = new SemaphoreSlim(
            settings.MaxConcurrentFileBatches, 
            settings.MaxConcurrentFileBatches);
        
        // 并发处理文件批次
        var tasks = fileBatches.Select(async (batch, batchIndex) =>
        {
            await concurrencyLimiter.WaitAsync();
            try
            {
                return await ProcessFileBatchConcurrently(
                    batch, batchIndex, fileBatches.Count, collectionName,
                    settings, progressCallback, totalFiles);
            }
            finally
            {
                concurrencyLimiter.Release();
            }
        });
        
        var batchResults = await Task.WhenAll(tasks);
        totalSnippets = batchResults.Sum(r => r.IndexedCount);
        processedFiles = batchResults.Sum(r => r.ProcessedFiles);
        
        _logger.LogInformation("并发批处理索引完成：共处理 {TotalFiles} 个文件，索引 {TotalSnippets} 个代码片段",
            totalFiles, totalSnippets);
        
        return totalSnippets;
    }

    /// <summary>
    /// 获取匹配的文件列表
    /// </summary>
    private List<string> GetMatchingFiles(string codebasePath, List<string> filePatterns)
    {
        var allFiles = new List<string>();
        foreach (var pattern in filePatterns)
        {
            var files = Directory.GetFiles(codebasePath, pattern, SearchOption.AllDirectories)
                .Where(f => !f.IsExcludedPath(new List<string> { "bin", "obj", ".git", "node_modules" }));
            allFiles.AddRange(files);
        }
        return allFiles;
    }

    /// <summary>
    /// 创建文件批次
    /// </summary>
    private List<List<string>> CreateFileBatches(List<string> files, int batchSize)
    {
        var batches = new List<List<string>>();
        for (int i = 0; i < files.Count; i += batchSize)
        {
            var batch = files.Skip(i).Take(batchSize).ToList();
            batches.Add(batch);
        }
        return batches;
    }

    /// <summary>
    /// 并发处理单个文件批次
    /// </summary>
    private async Task<(int IndexedCount, int ProcessedFiles)> ProcessFileBatchConcurrently(
        List<string> fileBatch,
        int batchIndex,
        int totalBatches,
        string collectionName,
        ConcurrencySettings settings,
        Func<int, int, string, Task>? progressCallback,
        int totalFiles)
    {
        var batchNumber = batchIndex + 1;
        _logger.LogDebug("开始处理批次 {BatchNumber}/{TotalBatches}，包含 {FileCount} 个文件",
            batchNumber, totalBatches, fileBatch.Count);
        
        var processedFilesInBatch = 0;
        
        try
        {
            // 并发解析文件
            var snippetTasks = fileBatch.Select(async filePath =>
            {
                var snippets = _searchService.ExtractCodeSnippets(filePath);
                _logger.LogTrace("文件 {FileName} 解析完成，提取 {Count} 个代码片段",
                    Path.GetFileName(filePath), snippets.Count);
                
                // 更新本批次处理的文件数
                Interlocked.Increment(ref processedFilesInBatch);
                
                // 调用进度回调（传递文件名信息）
                if (progressCallback != null)
                {
                    await progressCallback(0, totalFiles, Path.GetFileName(filePath)); // 0 作为占位符，实际计数在外层处理
                }
                
                return snippets;
            });
            
            var allSnippetsArrays = await Task.WhenAll(snippetTasks);
            var batchSnippets = allSnippetsArrays.SelectMany(s => s).ToList();
            
            // 并发索引当前批次的代码片段
            if (batchSnippets.Any())
            {
                var indexedCount = await _searchService.BatchIndexSnippetsConcurrentlyAsync(
                    batchSnippets, collectionName, settings);
                
                _logger.LogInformation("批次 {BatchNumber}/{TotalBatches} 并发索引完成：{SnippetCount} 个代码片段",
                    batchNumber, totalBatches, indexedCount);
                
                return (indexedCount, processedFilesInBatch);
            }
            else
            {
                _logger.LogWarning("批次 {BatchNumber}/{TotalBatches} 没有提取到代码片段",
                    batchNumber, totalBatches);
                return (0, processedFilesInBatch);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批次 {BatchNumber}/{TotalBatches} 并发处理失败",
                batchNumber, totalBatches);
            return (0, processedFilesInBatch);
        }
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
            
            // 从配置获取批处理设置
            var batchSize = 10; // 默认批大小
            var enableRealTimeProgress = true;
            var concurrencySettings = GetConcurrencySettings();

            // 使用并发批处理方法
            var indexedCount = await ProcessCodebaseInBatchesConcurrentlyAsync(
                task.CodebasePath,
                collectionName,
                new List<string> { "*.cs" },
                batchSize,
                concurrencySettings,
                async (processed, total, currentFile) => {
                    // 实时更新任务进度
                    if (enableRealTimeProgress)
                    {
                        task.CurrentFile = $"处理文件: {currentFile} ({processed}/{total})";
                        task.ProgressPercentage = 10 + (processed * 80 / total); // 10%-90%区间
                        await _persistenceService.UpdateTaskAsync(task);
                    }
                });
            
            // 🔥 新功能：填充 FileIndexDetails
            try
            {
                _logger.LogDebug("开始填充 FileIndexDetails 为代码库: {CodebasePath}", task.CodebasePath);
                var currentTime = DateTime.UtcNow;
                
                foreach (var filePath in codeFiles)
                {
                    var relativePath = filePath.GetRelativePath(task.CodebasePath);
                    var normalizedRelativePath = relativePath.NormalizePath();
                    
                    var fileDetail = new FileIndexDetail
                    {
                        FilePath = relativePath,
                        NormalizedFilePath = normalizedRelativePath,
                        LastIndexed = currentTime,
                        FileHash = null // 暂不实现文件哈希
                    };
                    
                    mapping.FileIndexDetails.Add(fileDetail);
                }
                
                _logger.LogInformation("成功填充 {Count} 个文件的索引详情", mapping.FileIndexDetails.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "填充 FileIndexDetails 时发生错误，但不影响索引完成");
            }
            
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
            
            // 🔥 新功能：索引完成后自动启动文件监控
            try
            {
                var fileWatcherService = GetFileWatcherService();
                var watcherCreated = await fileWatcherService.CreateWatcher(mapping);
                if (watcherCreated)
                {
                    _logger.LogInformation("索引完成后已自动启动文件监控: {FriendlyName} -> {CollectionName}",
                        mapping.FriendlyName, mapping.CollectionName);
                }
                else
                {
                    _logger.LogWarning("索引完成后启动文件监控失败: {Path}", mapping.CodebasePath);
                }
            }
            catch (Exception watcherEx)
            {
                _logger.LogError(watcherEx, "索引完成后启动文件监控时发生错误: {Path}", mapping.CodebasePath);
            }
            
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
        //验证文件变更刷新
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
    /// 增量重建索引 - 仅处理变更的文件
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

        var normalizedPath = codebasePath.NormalizePath();
        
        // 检查是否已在执行
        if (_runningTasks.ContainsKey(normalizedPath))
        {
            var existingTask = _runningTasks[normalizedPath];
            return new IndexingResult
            {
                Success = false,
                Message = "该代码库正在处理中，请等待完成",
                TaskId = existingTask.Id
            };
        }

        // 创建重建任务
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
        
        // 异步执行增量重建
        _ = Task.Run(async () => await ExecuteIncrementalRebuildAsync(task, mapping));
        
        _logger.LogInformation("增量重建任务已启动: {Path}, 任务ID: {TaskId}", codebasePath, task.Id);
        
        return new IndexingResult
        {
            Success = true,
            Message = "增量重建任务已启动",
            TaskId = task.Id
        };
    }

    /// <summary>
    /// 执行增量重建索引任务
    /// </summary>
    private async Task ExecuteIncrementalRebuildAsync(IndexingTask task, CodebaseMapping mapping)
    {
        var normalizedPath = task.CodebasePath.NormalizePath();
        var deletedFiles = 0;
        var newFiles = 0;
        var modifiedFiles = 0;
        var unchangedFiles = 0;
        
        try
        {
            _logger.LogInformation("开始执行增量重建: {Path}", task.CodebasePath);
            task.CurrentFile = "正在分析文件变更...";
            
            // 更新任务状态到持久化存储
            await _persistenceService.UpdateTaskAsync(task);
            
            // 检查Qdrant连接状态
            if (!_connectionMonitor.IsConnected)
            {
                _logger.LogWarning("Qdrant连接不可用，增量重建任务 {TaskId} 等待连接恢复", task.Id);
                task.CurrentFile = "等待数据库连接恢复...";
                task.Status = IndexingStatus.Pending;
                await _persistenceService.UpdateTaskAsync(task);
                
                var connectionRestored = await _connectionMonitor.WaitForConnectionAsync(task.Id);
                if (!connectionRestored)
                {
                    throw new InvalidOperationException("等待Qdrant连接超时，增量重建任务被取消");
                }
                
                _logger.LogInformation("Qdrant连接已恢复，继续执行增量重建任务 {TaskId}", task.Id);
                task.Status = IndexingStatus.Running;
                task.CurrentFile = "连接已恢复，继续分析...";
                await _persistenceService.UpdateTaskAsync(task);
            }
            
            // 获取当前物理文件列表
            task.CurrentFile = "正在扫描当前文件...";
            task.ProgressPercentage = 10;
            await _persistenceService.UpdateTaskAsync(task);
            
            var currentFiles = Directory.GetFiles(task.CodebasePath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.IsExcludedPath(new List<string> { "bin", "obj", ".git", "node_modules" }))
                .ToList();
            
            var currentFileRelativePaths = currentFiles
                .Select(f => f.GetRelativePath(task.CodebasePath).NormalizePath())
                .ToHashSet();
            
            _logger.LogInformation("当前物理文件数: {Count}", currentFiles.Count);
            
            // 处理已删除文件
            task.CurrentFile = "正在处理已删除文件...";
            task.ProgressPercentage = 20;
            await _persistenceService.UpdateTaskAsync(task);
            
            var filesToRemove = new List<FileIndexDetail>();
            foreach (var fileDetail in mapping.FileIndexDetails)
            {
                if (!currentFileRelativePaths.Contains(fileDetail.NormalizedFilePath))
                {
                    // 文件已被删除
                    _logger.LogDebug("文件已删除: {FilePath}", fileDetail.FilePath);
                    
                    // 从 Qdrant 中删除该文件的索引
                    var absolutePath = Path.Combine(task.CodebasePath, fileDetail.FilePath);
                    var deleteSuccess = await _searchService.DeleteFileIndexAsync(absolutePath, mapping.CollectionName);
                    if (deleteSuccess)
                    {
                        deletedFiles++;
                        filesToRemove.Add(fileDetail);
                        _logger.LogInformation("成功删除文件索引: {FilePath}", fileDetail.FilePath);
                    }
                    else
                    {
                        _logger.LogWarning("删除文件索引失败: {FilePath}", fileDetail.FilePath);
                    }
                }
            }
            
            // 从 FileIndexDetails 中移除已删除的文件记录
            foreach (var fileToRemove in filesToRemove)
            {
                mapping.FileIndexDetails.Remove(fileToRemove);
            }
            
            // 处理新增和修改的文件
            task.CurrentFile = "正在处理新增和修改的文件...";
            task.ProgressPercentage = 40;
            await _persistenceService.UpdateTaskAsync(task);
            
            var fileDetailDict = mapping.FileIndexDetails.ToDictionary(fd => fd.NormalizedFilePath, fd => fd);
            var processedFiles = 0;
            var totalFiles = currentFiles.Count;
            
            foreach (var filePath in currentFiles)
            {
                var relativePath = filePath.GetRelativePath(task.CodebasePath);
                var normalizedRelativePath = relativePath.NormalizePath();
                
                task.CurrentFile = $"处理文件: {relativePath}";
                task.ProgressPercentage = 40 + (processedFiles * 50 / totalFiles);
                await _persistenceService.UpdateTaskAsync(task);
                
                if (fileDetailDict.TryGetValue(normalizedRelativePath, out var existingDetail))
                {
                    // 文件已存在，检查是否修改
                    var currentLastWriteTime = File.GetLastWriteTimeUtc(filePath);
                    
                    if (currentLastWriteTime > existingDetail.LastIndexed)
                    {
                        // 文件已修改，需要重新索引
                        _logger.LogDebug("文件已修改: {FilePath}", relativePath);
                        
                        var updateSuccess = await UpdateFileIndexAsync(filePath, mapping.CollectionName);
                        if (updateSuccess)
                        {
                            modifiedFiles++;
                            existingDetail.LastIndexed = DateTime.UtcNow;
                            _logger.LogInformation("成功更新文件索引: {FilePath}", relativePath);
                        }
                        else
                        {
                            _logger.LogWarning("更新文件索引失败: {FilePath}", relativePath);
                        }
                    }
                    else
                    {
                        // 文件未修改
                        unchangedFiles++;
                        _logger.LogDebug("文件未修改: {FilePath}", relativePath);
                    }
                }
                else
                {
                    // 新增文件，需要索引
                    _logger.LogDebug("发现新增文件: {FilePath}", relativePath);
                    
                    var snippets = _searchService.ExtractCSharpSnippets(filePath);
                    if (snippets.Any())
                    {
                        await _searchService.BatchIndexSnippetsAsync(snippets, mapping.CollectionName);
                        newFiles++;
                        
                        // 添加到 FileIndexDetails
                        var newFileDetail = new FileIndexDetail
                        {
                            FilePath = relativePath,
                            NormalizedFilePath = normalizedRelativePath,
                            LastIndexed = DateTime.UtcNow,
                            FileHash = null
                        };
                        
                        mapping.FileIndexDetails.Add(newFileDetail);
                        _logger.LogInformation("成功索引新增文件: {FilePath}, 片段数: {Count}", relativePath, snippets.Count);
                    }
                    else
                    {
                        _logger.LogDebug("新增文件 {FilePath} 没有提取到代码片段", relativePath);
                    }
                }
                
                processedFiles++;
            }
            
            // 更新任务状态
            task.Status = IndexingStatus.Completed;
            task.EndTime = DateTime.UtcNow;
            task.ProgressPercentage = 100;
            task.CurrentFile = "增量重建完成";
            
            // 更新映射状态
            mapping.IndexingStatus = "completed";
            mapping.LastIndexed = DateTime.UtcNow;
            mapping.Statistics.TotalFiles = currentFiles.Count;
            mapping.Statistics.LastIndexingDuration = $"{(task.EndTime - task.StartTime)?.TotalSeconds:F1}s";
            mapping.Statistics.LastUpdateTime = DateTime.UtcNow;
            
            await _configManager.UpdateMapping(mapping);
            
            // 清理已完成的任务
            await _persistenceService.CleanupTaskAsync(task.Id);
            
            _logger.LogInformation("增量重建任务完成: {Path}, 删除:{Deleted}, 新增:{New}, 修改:{Modified}, 未变:{Unchanged}, 耗时:{Duration}s",
                task.CodebasePath, deletedFiles, newFiles, modifiedFiles, unchangedFiles,
                (task.EndTime - task.StartTime)?.TotalSeconds);
        }
        catch (Exception ex)
        {
            task.Status = IndexingStatus.Failed;
            task.ErrorMessage = ex.Message;
            task.EndTime = DateTime.UtcNow;
            task.CurrentFile = "增量重建失败";
            
            // 更新失败状态到持久化存储
            await _persistenceService.UpdateTaskAsync(task);
            
            _logger.LogError(ex, "增量重建任务失败: {Path}", task.CodebasePath);
            
            // 尝试更新映射状态为失败
            try
            {
                mapping.IndexingStatus = "failed";
                await _configManager.UpdateMapping(mapping);
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, "更新失败状态时出错");
            }
            
            // 延迟清理失败的任务
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
            
            // 🔥 先删除文件的旧索引
            var deleteSuccess = await _searchService.DeleteFileIndexAsync(filePath, collectionName);
            if (!deleteSuccess)
            {
                _logger.LogWarning("删除文件旧索引失败，但继续更新: {FilePath}", filePath);
            }
            
            var snippets = _searchService.ExtractCSharpSnippets(filePath);
            if (snippets.Any())
            {
                await _searchService.BatchIndexSnippetsAsync(snippets, collectionName);
                _logger.LogInformation("文件索引更新完成: {FilePath}, 片段数: {Count}", filePath, snippets.Count);
                
                // 🔥 新功能：同步更新 FileIndexDetails
                await UpdateFileIndexDetailsAsync(filePath, collectionName);
                
                return true;
            }
            else
            {
                _logger.LogDebug("文件 {FilePath} 没有提取到代码片段", filePath);
                
                // 🔥 新功能：即使没有片段，也要更新 FileIndexDetails
                await UpdateFileIndexDetailsAsync(filePath, collectionName);
                
                return true; // 删除成功但没有新内容也算成功
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新文件索引失败: {FilePath}", filePath);
            return false;
        }
    }
    
    /// <summary>
    /// 更新文件的索引详情记录
    /// </summary>
    private async Task UpdateFileIndexDetailsAsync(string filePath, string collectionName)
    {
        try
        {
            // 根据 collectionName 找到对应的 CodebaseMapping
            var mapping = _configManager.GetAllMappings().FirstOrDefault(m => m.CollectionName == collectionName);
            if (mapping == null)
            {
                _logger.LogWarning("无法找到集合 {CollectionName} 对应的映射", collectionName);
                return;
            }
            
            var relativePath = filePath.GetRelativePath(mapping.CodebasePath);
            var normalizedRelativePath = relativePath.NormalizePath();
            
            // 查找或创建 FileIndexDetail
            var existingDetail = mapping.FileIndexDetails.FirstOrDefault(fd => fd.NormalizedFilePath == normalizedRelativePath);
            if (existingDetail != null)
            {
                // 更新现有记录
                existingDetail.LastIndexed = DateTime.UtcNow;
                _logger.LogDebug("更新 FileIndexDetail: {FilePath}", relativePath);
            }
            else
            {
                // 创建新记录（用于新增文件）
                var newDetail = new FileIndexDetail
                {
                    FilePath = relativePath,
                    NormalizedFilePath = normalizedRelativePath,
                    LastIndexed = DateTime.UtcNow,
                    FileHash = null
                };
                
                mapping.FileIndexDetails.Add(newDetail);
                _logger.LogDebug("创建新 FileIndexDetail: {FilePath}", relativePath);
            }
            
            // 保存映射更改
            await _configManager.UpdateMapping(mapping);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新 FileIndexDetails 失败: {FilePath}", filePath);
        }
    }
    
    /// <summary>
    /// 删除文件的索引详情记录
    /// </summary>
    private async Task RemoveFileIndexDetailsAsync(string filePath, string collectionName)
    {
        try
        {
            // 根据 collectionName 找到对应的 CodebaseMapping
            var mapping = _configManager.GetAllMappings().FirstOrDefault(m => m.CollectionName == collectionName);
            if (mapping == null)
            {
                _logger.LogWarning("无法找到集合 {CollectionName} 对应的映射", collectionName);
                return;
            }
            
            var relativePath = filePath.GetRelativePath(mapping.CodebasePath);
            var normalizedRelativePath = relativePath.NormalizePath();
            
            // 查找并移除 FileIndexDetail
            var detailToRemove = mapping.FileIndexDetails.FirstOrDefault(fd => fd.NormalizedFilePath == normalizedRelativePath);
            if (detailToRemove != null)
            {
                mapping.FileIndexDetails.Remove(detailToRemove);
                _logger.LogDebug("移除 FileIndexDetail: {FilePath}", relativePath);
                
                // 保存映射更改
                await _configManager.UpdateMapping(mapping);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "移除 FileIndexDetails 失败: {FilePath}", filePath);
        }
    }

    /// <summary>
    /// 处理文件删除事件，清理Qdrant索引和元数据
    /// </summary>
    public async Task<bool> HandleFileDeletionAsync(string filePath, string collectionName)
    {
        _logger.LogInformation("处理文件删除事件: {FilePath} from collection {CollectionName}", filePath, collectionName);
        bool qdrantDeleteSuccess = await _searchService.DeleteFileIndexAsync(filePath, collectionName);
        if (!qdrantDeleteSuccess)
        {
            _logger.LogWarning("从 Qdrant 删除文件索引失败: {FilePath}", filePath);
            // 根据策略，这里可以选择是否继续清理元数据，或者直接返回false以指示部分失败
        }

        // 尝试清理元数据，即使Qdrant删除可能失败，以避免孤立的元数据记录
        // 如果需要更严格的事务性，可以仅在qdrantDeleteSuccess为true时执行此操作
        await RemoveFileIndexDetailsAsync(filePath, collectionName); 
        
        _logger.LogInformation("文件删除事件处理完成: {FilePath}, Qdrant删除状态: {Status}", filePath, qdrantDeleteSuccess);
        return qdrantDeleteSuccess; // 主要返回Qdrant操作的状态，元数据清理失败会记录日志
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

    /// <summary>
    /// 删除索引库 - 安全确认模式
    /// </summary>
    public async Task<(bool Success, string Message)> DeleteIndexLibraryAsync(
        string codebasePath,
        bool confirm = false)
    {
        try
        {
            // 1. 验证和获取映射
            var normalizedPath = Path.GetFullPath(codebasePath);
            var mapping = _configManager.GetMappingByPath(normalizedPath);
            
            if (mapping == null)
            {
                return (false, $"❌ 代码库索引不存在: {normalizedPath}");
            }

            // 2. 如果未确认，显示详细信息
            if (!confirm)
            {
                return (false, GenerateConfirmationMessage(mapping));
            }

            // 3. 执行删除流程
            var result = await ExecuteDeleteProcess(mapping);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除索引库时发生错误: {CodebasePath}", codebasePath);
            return (false, $"❌ 删除过程中发生错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 生成删除确认信息
    /// </summary>
    private string GenerateConfirmationMessage(CodebaseMapping mapping)
    {
        var confirmationMessage = $"🗑️ 即将删除索引库\n\n" +
                                 $"📁 代码库路径: {mapping.CodebasePath}\n" +
                                 $"🏷️ 友好名称: {mapping.FriendlyName}\n" +
                                 $"📊 集合名称: {mapping.CollectionName}\n" +
                                 $"📦 代码片段数: {mapping.Statistics.IndexedSnippets:N0}个\n" +
                                 $"📄 文件数: {mapping.Statistics.TotalFiles:N0}个\n" +
                                 $"📅 创建时间: {mapping.CreatedAt:yyyy-MM-dd HH:mm:ss}\n";

        if (mapping.Statistics.LastUpdateTime.HasValue)
        {
            confirmationMessage += $"📅 最后更新: {mapping.Statistics.LastUpdateTime:yyyy-MM-dd HH:mm:ss}\n";
        }

        confirmationMessage += $"👁️ 监控状态: {(mapping.IsMonitoring ? "启用" : "禁用")}\n" +
                              $"🔄 索引状态: {mapping.IndexingStatus}\n\n" +
                              $"⚠️ 警告: 此操作不可逆！删除后需要重新创建索引才能搜索此代码库。\n\n" +
                              $"✅ 将执行以下操作:\n" +
                              $"  1. 停止文件监控服务\n" +
                              $"  2. 删除 Qdrant 集合及所有向量数据\n" +
                              $"  3. 清理任务持久化记录\n" +
                              $"  4. 移除本地配置映射\n\n" +
                              $"💡 如需确认删除，请设置 confirm=true 参数";

        return confirmationMessage;
    }

    /// <summary>
    /// 执行删除流程
    /// </summary>
    private async Task<(bool Success, string Message)> ExecuteDeleteProcess(CodebaseMapping mapping)
    {
        var steps = new List<string>();
        var hasErrors = false;
        
        try
        {
            _logger.LogInformation("开始执行索引库删除流程: {FriendlyName} ({CollectionName})",
                mapping.FriendlyName, mapping.CollectionName);

            // 1. 停止运行中的任务
            try
            {
                await StopRunningTasks(mapping.CodebasePath);
                steps.Add("✅ 停止运行中的索引任务");
            }
            catch (Exception ex)
            {
                steps.Add($"⚠️ 停止索引任务时发生警告: {ex.Message}");
                hasErrors = true;
            }

            // 2. 停止文件监控
            try
            {
                var fileWatcherService = GetFileWatcherService();
                var stopResult = fileWatcherService.StopWatcher(mapping.Id);
                if (stopResult)
                {
                    steps.Add("✅ 停止文件监控服务");
                }
                else
                {
                    steps.Add("⚠️ 文件监控服务停止失败（可能未启动）");
                    hasErrors = true;
                }
            }
            catch (Exception ex)
            {
                steps.Add($"⚠️ 停止文件监控时发生错误: {ex.Message}");
                hasErrors = true;
            }

            // 3. 删除 Qdrant 集合
            try
            {
                var deleteSuccess = await _searchService.DeleteCollectionAsync(mapping.CollectionName);
                if (deleteSuccess)
                {
                    steps.Add("✅ 删除 Qdrant 集合数据");
                }
                else
                {
                    steps.Add("⚠️ Qdrant 集合删除失败（可能已不存在）");
                    hasErrors = true;
                }
            }
            catch (Exception ex)
            {
                steps.Add($"❌ 删除 Qdrant 集合时发生错误: {ex.Message}");
                hasErrors = true;
            }

            // 4. 清理任务持久化记录
            try
            {
                await CleanupTaskRecords(mapping.CodebasePath);
                steps.Add("✅ 清理任务持久化记录");
            }
            catch (Exception ex)
            {
                steps.Add($"⚠️ 清理任务记录时发生警告: {ex.Message}");
                hasErrors = true;
            }

            // 5. 删除配置映射
            try
            {
                var configDeleteSuccess = await _configManager.RemoveMappingByPath(mapping.CodebasePath);
                if (configDeleteSuccess)
                {
                    steps.Add("✅ 移除配置映射");
                }
                else
                {
                    steps.Add("❌ 移除配置映射失败");
                    hasErrors = true;
                }
            }
            catch (Exception ex)
            {
                steps.Add($"❌ 移除配置映射时发生错误: {ex.Message}");
                hasErrors = true;
            }

            var statusIcon = hasErrors ? "⚠️" : "🗑️";
            var statusText = hasErrors ? "索引库删除部分完成" : "索引库删除完成";
            
            var message = $"{statusIcon} {statusText}\n\n" +
                         $"📁 代码库: {mapping.FriendlyName}\n" +
                         $"📊 集合: {mapping.CollectionName}\n\n" +
                         $"执行步骤:\n{string.Join("\n", steps)}";

            return (!hasErrors, message);
        }
        catch (Exception ex)
        {
            steps.Add($"❌ 删除过程中发生严重错误: {ex.Message}");
            var message = $"❌ 索引库删除失败\n\n执行步骤:\n{string.Join("\n", steps)}";
            return (false, message);
        }
    }

    /// <summary>
    /// 停止指定代码库的运行中任务
    /// </summary>
    private async Task StopRunningTasks(string codebasePath)
    {
        var normalizedPath = codebasePath.NormalizePath();
        
        if (_runningTasks.TryGetValue(normalizedPath, out var runningTask))
        {
            _logger.LogInformation("发现运行中的任务，正在停止: {TaskId}", runningTask.Id);
            await CancelTaskAsync(runningTask.Id);
            
            // 等待任务完全停止
            var maxWait = TimeSpan.FromSeconds(10);
            var waited = TimeSpan.Zero;
            while (_runningTasks.ContainsKey(normalizedPath) && waited < maxWait)
            {
                await Task.Delay(500);
                waited = waited.Add(TimeSpan.FromMilliseconds(500));
            }
            
            if (_runningTasks.ContainsKey(normalizedPath))
            {
                _logger.LogWarning("任务停止超时，强制移除: {TaskId}", runningTask.Id);
                _runningTasks.TryRemove(normalizedPath, out _);
            }
        }
    }

    /// <summary>
    /// 清理指定代码库的任务持久化记录
    /// </summary>
    private async Task CleanupTaskRecords(string codebasePath)
    {
        try
        {
            // 获取与此代码库相关的所有任务记录
            var allTasks = await _persistenceService.LoadPendingTasksAsync();
            var tasksToCleanup = allTasks
                .Where(t => Path.GetFullPath(t.CodebasePath).Equals(Path.GetFullPath(codebasePath), StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var task in tasksToCleanup)
            {
                await _persistenceService.CleanupTaskAsync(task.Id);
                _logger.LogDebug("清理任务记录: {TaskId}", task.Id);
            }

            if (tasksToCleanup.Any())
            {
                _logger.LogInformation("清理了 {Count} 个相关任务记录", tasksToCleanup.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理任务记录时发生错误: {CodebasePath}", codebasePath);
            throw;
        }
    }
}