using System.Threading.Channels;
using CodebaseMcpServer.Models;
using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Data.Repositories;

namespace CodebaseMcpServer.Services.Domain;

/// <summary>
/// 后台任务服务 - 负责处理所有长时间运行的任务
/// </summary>
public class BackgroundTaskService : BackgroundService, IBackgroundTaskService
{
    private readonly ILogger<BackgroundTaskService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Channel<int> _taskQueue;
    private readonly ConcurrencySettings _concurrencySettings;

    public BackgroundTaskService(
        ILogger<BackgroundTaskService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _concurrencySettings = configuration.GetSection("ConcurrencySettings").Get<ConcurrencySettings>() ?? new ConcurrencySettings();

        var options = new BoundedChannelOptions(_concurrencySettings.MaxQueuedTasks)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        _taskQueue = Channel.CreateBounded<int>(options);
    }

    public async Task<string> QueueIndexingTaskAsync(int libraryId, TaskPriority priority)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBackgroundTaskRepository>();

        var task = new BackgroundTask
        {
            Type = BackgroundTaskType.Indexing,
            LibraryId = libraryId,
            Status = BackgroundTaskStatus.Pending,
            Priority = priority
        };

        var createdTask = await repository.CreateAsync(task);
        await _taskQueue.Writer.WriteAsync(createdTask.Id);
        
        _logger.LogInformation("索引任务已排队: TaskId={TaskId}, LibraryId={LibraryId}", createdTask.TaskId, libraryId);
        return createdTask.TaskId;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("后台任务服务已启动，最大并发数: {MaxConcurrency}", _concurrencySettings.MaxConcurrentTasks);
        var semaphore = new SemaphoreSlim(_concurrencySettings.MaxConcurrentTasks, _concurrencySettings.MaxConcurrentTasks);

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("后台任务循环正在等待任务...");
            try
            {
                await _taskQueue.Reader.WaitToReadAsync(stoppingToken);
                _logger.LogDebug("任务队列中有新项目");

                var taskId = await _taskQueue.Reader.ReadAsync(stoppingToken);
                _logger.LogDebug("已从队列中读取任务ID: {TaskId}", taskId);

                await semaphore.WaitAsync(stoppingToken);
                _logger.LogDebug("已获取信号量，准备处理任务: {TaskId}", taskId);

                _ = Task.Run(async () =>
                {
                    _logger.LogDebug("启动新线程处理任务: {TaskId}", taskId);
                    try
                    {
                        await ProcessTaskAsync(taskId, stoppingToken);
                    }
                    finally
                    {
                        semaphore.Release();
                        _logger.LogDebug("已释放信号量，任务完成: {TaskId}", taskId);
                    }
                }, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("后台任务服务正在停止...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "后台任务服务发生未处理的异常");
            }
        }
    }

    private async Task ProcessTaskAsync(int taskId, CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IBackgroundTaskRepository>();
        
        var task = await repository.GetByIdAsync(taskId);
        if (task == null)
        {
            _logger.LogWarning("无法找到要处理的任务: ID={TaskId}", taskId);
            return;
        }

        _logger.LogInformation("开始处理任务: {TaskId} ({Type}) for Library {LibraryId}", task.TaskId, task.Type, task.LibraryId);
        
        task.Status = BackgroundTaskStatus.Running;
        task.StartedAt = DateTime.UtcNow;
        await repository.UpdateAsync(task);

        bool success = false;
        try
        {
            success = task.Type switch
            {
                BackgroundTaskType.Indexing => await ProcessIndexingTaskAsync(scope, task, stoppingToken),
                // 其他任务类型可以在这里添加
                _ => throw new NotSupportedException($"不支持的任务类型: {task.Type}")
            };
            
            task.Status = success ? BackgroundTaskStatus.Completed : BackgroundTaskStatus.Failed;
            if (!success && string.IsNullOrEmpty(task.ErrorMessage))
            {
                task.ErrorMessage = "任务执行失败，但未提供具体错误信息。";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "任务处理失败: {TaskId}", task.TaskId);
            task.Status = BackgroundTaskStatus.Failed;
            task.ErrorMessage = ex.Message;
        }
        finally
        {
            task.CompletedAt = DateTime.UtcNow;
            task.Progress = 100;
            await repository.UpdateAsync(task);
            _logger.LogInformation("任务处理完成: {TaskId}, 状态: {Status}", task.TaskId, task.Status);
        }
    }

    private async Task<bool> ProcessIndexingTaskAsync(IServiceScope scope, BackgroundTask task, CancellationToken stoppingToken)
    {
        if (!task.LibraryId.HasValue)
        {
            task.ErrorMessage = "任务缺少 LibraryId";
            return false;
        }

        var libraryRepository = scope.ServiceProvider.GetRequiredService<IIndexLibraryRepository>();
        var searchService = scope.ServiceProvider.GetRequiredService<EnhancedCodeSemanticSearch>();
        
        var library = await libraryRepository.GetByIdAsync(task.LibraryId.Value);
        if (library == null)
        {
            task.ErrorMessage = $"找不到索引库: {task.LibraryId.Value}";
            return false;
        }

        try
        {
            library.Status = IndexLibraryStatus.Indexing;
            await libraryRepository.UpdateAsync(library);

            var watchConfig = library.WatchConfigObject;
            var allFiles = GetMatchingFiles(library.CodebasePath, watchConfig.FilePatterns, watchConfig.ExcludePatterns, watchConfig.IncludeSubdirectories);
            
            if (!allFiles.Any())
            {
                _logger.LogWarning("在代码库 {Path} 中未找到与模式匹配的文件", library.CodebasePath);
                library.Status = IndexLibraryStatus.Completed;
                library.TotalFiles = 0;
                library.IndexedSnippets = 0;
                await libraryRepository.UpdateAsync(library);
                return true;
            }

            await searchService.EnsureCollectionAsync(library.CollectionName);

            int totalFiles = allFiles.Count;
            int processedFiles = 0;
            int totalSnippets = 0;
            int batchSize = 10;

            for (int i = 0; i < totalFiles; i += batchSize)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    task.ErrorMessage = "任务被取消";
                    library.Status = IndexLibraryStatus.Cancelled;
                    await libraryRepository.UpdateAsync(library);
                    return false;
                }

                var batch = allFiles.Skip(i).Take(batchSize).ToList();
                var batchSnippets = new List<CodeSnippet>();

                foreach (var filePath in batch)
                {
                    var snippets = searchService.ExtractCodeSnippets(filePath);
                    batchSnippets.AddRange(snippets);
                    processedFiles++;
                }

                if (batchSnippets.Any())
                {
                    await searchService.BatchIndexSnippetsAsync(batchSnippets, library.CollectionName);
                    totalSnippets += batchSnippets.Count;
                }
                
                task.Progress = (int)((double)processedFiles / totalFiles * 100);
                task.CurrentFile = $"处理中: {processedFiles}/{totalFiles}";
                await scope.ServiceProvider.GetRequiredService<IBackgroundTaskRepository>().UpdateAsync(task);
            }

            library.Status = IndexLibraryStatus.Completed;
            library.TotalFiles = totalFiles;
            library.IndexedSnippets = totalSnippets;
            library.LastIndexedAt = DateTime.UtcNow;
            await libraryRepository.UpdateAsync(library);

            return true;
        }
        catch (Exception ex)
        {
            library.Status = IndexLibraryStatus.Failed;
            await libraryRepository.UpdateAsync(library);
            task.ErrorMessage = ex.Message;
            _logger.LogError(ex, "索引任务执行失败: LibraryId={LibraryId}", library.Id);
            return false;
        }
    }

    private List<string> GetMatchingFiles(string basePath, List<string> includePatterns, List<string> excludePatterns, bool includeSubdirectories)
    {
        var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var allFiles = new List<string>();
        
        foreach (var pattern in includePatterns)
        {
            allFiles.AddRange(Directory.GetFiles(basePath, pattern, searchOption));
        }
        
        allFiles = allFiles.Distinct().ToList();
        
        if (excludePatterns.Any())
        {
            allFiles = allFiles.Where(file => 
                !excludePatterns.Any(exclude => 
                    Path.GetRelativePath(basePath, file).Contains(exclude, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }
        
        return allFiles;
    }
}