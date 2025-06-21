using System.Collections.Concurrent;
using System.IO;
using CodebaseMcpServer.Models;
using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Domain;
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
    private readonly IBackgroundTaskService _backgroundTaskService;

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IServiceProvider serviceProvider,
        IBackgroundTaskService backgroundTaskService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _backgroundTaskService = backgroundTaskService;
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
          // 排除数据库文件
        var fileName = Path.GetFileName(fullPath);
        if (fileName.Contains("codebase-app.db"))
        {
            _logger.LogDebug("数据库文件变更，已跳过: {Path}", fullPath);
            return;
        }
        _logger.LogInformation("检测到文件变更: LibraryId={LibraryId}, Type={ChangeType}, Path={Path}", libraryId, changeType, fullPath);

        using var scope = _serviceProvider.CreateScope();
        var libraryRepository = scope.ServiceProvider.GetRequiredService<IIndexLibraryRepository>();
        var library = await libraryRepository.GetByIdAsync(libraryId);

        if (library == null)
        {
            _logger.LogWarning("处理文件变更时找不到索引库: LibraryId={LibraryId}", libraryId);
            return;
        }

        // 检查文件是否符合被索引的模式
        var watchConfig = library.WatchConfigObject;
        if (!IsFileMatch(fullPath, watchConfig))
        {
            _logger.LogDebug("文件 {Path} 不匹配索引模式，已跳过。", fullPath);
            return;
        }

        switch (changeType)
        {
            case Models.FileChangeType.Created:
            case Models.FileChangeType.Modified:
                await _backgroundTaskService.QueueFileUpdateTaskAsync(libraryId, fullPath);
                break;
            case Models.FileChangeType.Deleted:
                await _backgroundTaskService.QueueFileDeleteTaskAsync(libraryId, fullPath);
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
        // 排除数据库文件
        var oldFileName = Path.GetFileName(oldFullPath);

        var newFileName = Path.GetFileName(newFullPath);
        if (oldFileName.Contains("codebase-app.db") || newFileName.Contains("codebase-app.db"))
        {
            _logger.LogDebug("数据库文件重命名，已跳过: Old={OldPath}, New={NewPath}", oldFullPath, newFullPath);
            return;
        }

        // 检查新文件是否符合被索引的模式
        var watchConfig = library.WatchConfigObject;
        if (IsFileMatch(newFullPath, watchConfig))
        {
            _logger.LogDebug("重命名后的文件 {Path} 匹配索引模式，将进行处理。", newFullPath);
            // 将重命名视为一次删除和一次创建
            await _backgroundTaskService.QueueFileDeleteTaskAsync(libraryId, oldFullPath);
            await _backgroundTaskService.QueueFileUpdateTaskAsync(libraryId, newFullPath);
        }
        else
        {
            _logger.LogDebug("重命名后的文件 {Path} 不匹配索引模式，仅处理删除。", newFullPath);
            // 如果新文件不匹配，只处理旧文件的删除
            await _backgroundTaskService.QueueFileDeleteTaskAsync(libraryId, oldFullPath);
        }
    }

    private void OnWatcherError(int libraryId, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "文件监控器发生错误: LibraryId={LibraryId}", libraryId);
    }

    private bool IsFileMatch(string filePath, WatchConfigurationDto config)
    {
        var fileName = Path.GetFileName(filePath);
        var directoryName = Path.GetDirectoryName(filePath);

        // 1. 检查是否在排除列表（检查完整路径和目录）
        if (config.ExcludePatterns.Any(p => filePath.Contains(p, StringComparison.OrdinalIgnoreCase) || (directoryName != null && directoryName.Contains(p, StringComparison.OrdinalIgnoreCase))))
        {
            return false;
        }

        // 2. 如果文件模式列表为空，则默认匹配所有文件
        if (config.FilePatterns == null || !config.FilePatterns.Any())
        {
            return true;
        }

        // 3. 检查是否匹配任何文件模式（检查文件名后缀）
        return config.FilePatterns.Any(p => fileName.EndsWith(p, StringComparison.OrdinalIgnoreCase));
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
