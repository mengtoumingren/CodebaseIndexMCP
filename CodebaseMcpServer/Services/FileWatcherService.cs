using System.Collections.Concurrent;
using CodebaseMcpServer.Models;
using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Data.Repositories;

namespace CodebaseMcpServer.Services;

/// <summary>
/// 文件监控服务 - 监控代码库文件变更并自动更新索引
/// </summary>
public class FileWatcherService : BackgroundService
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<int, FileSystemWatcher> _watchers = new();
    private readonly IndexingTaskManager _indexingTaskManager;

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IServiceProvider serviceProvider,
        IndexingTaskManager indexingTaskManager)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _indexingTaskManager = indexingTaskManager;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("文件监控服务启动");

        // 关键修复：重新引入启动延迟以解决与数据迁移的竞态条件
        _logger.LogInformation("文件监控服务将延迟15秒以等待应用初始化...");
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("文件监控服务在启动延迟期间被取消。");
            return;
        }

        // 定期检查新的或更新的索引库
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshWatchersAsync(stoppingToken);
            
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break; // 退出循环
            }
        }
        
        _logger.LogInformation("文件监控服务已停止。");
    }

    public async Task RefreshWatchersAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var libraryRepository = scope.ServiceProvider.GetRequiredService<IIndexLibraryRepository>();
        
        _logger.LogInformation("正在刷新文件监控器...");
        var librariesToWatch = await libraryRepository.GetLibrariesForMonitoringAsync();

        // 移除不再需要监控的
        var librariesToRemove = _watchers.Keys.Except(librariesToWatch.Select(l => l.Id)).ToList();
        foreach (var libraryId in librariesToRemove)
        {
            if (_watchers.TryRemove(libraryId, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _logger.LogInformation("已停止监控索引库: {LibraryId}", libraryId);
            }
        }

        // 添加或更新现有的
        foreach (var library in librariesToWatch)
        {
            if (stoppingToken.IsCancellationRequested) return;

            if (_watchers.ContainsKey(library.Id))
            {
                // TODO: 实现更新现有监控器的逻辑，例如当FilePatterns改变时
                continue;
            }

            CreateWatcher(library);
        }
    }

    public void CreateWatcher(IndexLibrary library)
    {
        try
        {
            if (!Directory.Exists(library.CodebasePath))
            {
                _logger.LogWarning("监控目录不存在，跳过: {Path}", library.CodebasePath);
                return;
            }

            var watchConfig = library.WatchConfigObject;
            if (!watchConfig.IsEnabled)
            {
                return;
            }

            var watcher = new FileSystemWatcher(library.CodebasePath)
            {
                IncludeSubdirectories = watchConfig.IncludeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.DirectoryName
            };
            
            // 绑定事件处理器
            watcher.Created += (s, e) => OnFileChanged(library.Id, e.FullPath, FileChangeType.Created);
            watcher.Changed += (s, e) => OnFileChanged(library.Id, e.FullPath, FileChangeType.Modified);
            watcher.Deleted += (s, e) => OnFileChanged(library.Id, e.FullPath, FileChangeType.Deleted);
            watcher.Renamed += (s, e) => OnFileRenamed(library.Id, e.OldFullPath, e.FullPath);
            watcher.Error += (s, e) => OnWatcherError(library.Id, e);

            watcher.EnableRaisingEvents = true;
            _watchers[library.Id] = watcher;

            _logger.LogInformation("开始监控索引库: {LibraryName} (ID: {LibraryId})", library.Name, library.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建文件监控器失败: LibraryId={LibraryId}", library.Id);
        }
    }

    private async void OnFileChanged(int libraryId, string fullPath, Models.FileChangeType changeType)
    {
        _logger.LogInformation("检测到文件变更: LibraryId={LibraryId}, Type={ChangeType}, Path={Path}", libraryId, changeType, fullPath);

        using var scope = _serviceProvider.CreateScope();
        var libraryRepository = scope.ServiceProvider.GetRequiredService<IIndexLibraryRepository>();
        var library = await libraryRepository.GetByIdAsync(libraryId);

        if (library == null)
        {
            _logger.LogWarning("处理文件变更时找不到索引库: LibraryId={LibraryId}", libraryId);
            return;
        }

        // 可以在这里添加逻辑，检查文件是否符合被索引的模式
        // var watchConfig = library.WatchConfigObject;
        // if (!IsFileMatch(fullPath, watchConfig.FilePatterns, watchConfig.ExcludePatterns))
        // {
        //     _logger.LogDebug("文件 {Path} 不匹配索引模式，已跳过。", fullPath);
        //     return;
        // }

        switch (changeType)
        {
            case Models.FileChangeType.Created:
            case Models.FileChangeType.Modified:
                await _indexingTaskManager.UpdateFileIndexAsync(fullPath, library.CollectionName);
                break;
            case Models.FileChangeType.Deleted:
                await _indexingTaskManager.HandleFileDeletionAsync(fullPath, library.CollectionName);
                break;
        }
    }

    private async void OnFileRenamed(int libraryId, string oldFullPath, string newFullPath)
    {
        _logger.LogInformation("检测到文件重命名: LibraryId={LibraryId}, Old={OldPath}, New={NewPath}", libraryId, oldFullPath, newFullPath);

        using var scope = _serviceProvider.CreateScope();
        var libraryRepository = scope.ServiceProvider.GetRequiredService<IIndexLibraryRepository>();
        var library = await libraryRepository.GetByIdAsync(libraryId);

        if (library == null)
        {
            _logger.LogWarning("处理文件重命名时找不到索引库: LibraryId={LibraryId}", libraryId);
            return;
        }

        // 将重命名视为一次删除和一次创建
        await _indexingTaskManager.HandleFileDeletionAsync(oldFullPath, library.CollectionName);
        await _indexingTaskManager.UpdateFileIndexAsync(newFullPath, library.CollectionName);
    }

    private void OnWatcherError(int libraryId, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "文件监控器发生错误: LibraryId={LibraryId}", libraryId);
    }

    public bool StopWatcher(int libraryId)
    {
        if (_watchers.TryRemove(libraryId, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
            _logger.LogInformation("已停止监控索引库: {LibraryId}", libraryId);
            return true;
        }
        return false;
    }

    public override void Dispose()
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }
        base.Dispose();
    }
}
