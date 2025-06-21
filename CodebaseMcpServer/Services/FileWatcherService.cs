using System.Collections.Concurrent;
using System.IO;
using CodebaseMcpServer.Models;
using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Domain;
using CodebaseMcpServer.Services.Data.Repositories;
using Microsoft.Extensions.Configuration;
using System.Threading;

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
    private readonly ConcurrentDictionary<string, Timer> _debouncedEvents = new();
    private readonly TimeSpan _debounceDelay;

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IServiceProvider serviceProvider,
        IBackgroundTaskService backgroundTaskService,
        IConfiguration configuration)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _backgroundTaskService = backgroundTaskService;
        _debounceDelay = TimeSpan.FromMilliseconds(
            configuration.GetValue<int>("FileWatcher:DebounceTime", 500)
        );
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
            watcher.Created += (s, e) => DebounceEvent(library.Id, e.FullPath);
            watcher.Changed += (s, e) => DebounceEvent(library.Id, e.FullPath);
            watcher.Deleted += (s, e) => DebounceEvent(library.Id, e.FullPath);
            watcher.Renamed += (s, e) => {
                // 将重命名视为旧路径的删除和新路径的创建/修改
                DebounceEvent(library.Id, e.OldFullPath);
                DebounceEvent(library.Id, e.FullPath);
            };
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

    private void DebounceEvent(int libraryId, string fullPath)
    {
        // 排除数据库文件
        var fileName = Path.GetFileName(fullPath);
        if (fileName.Contains("codebase-app.db"))
        {
            _logger.LogTrace("数据库文件变更，已跳过: {Path}", fullPath);
            return;
        }

        _logger.LogTrace("检测到原始文件变更: LibraryId={LibraryId}, Path={Path}", libraryId, fullPath);

        if (_debouncedEvents.TryGetValue(fullPath, out var timer))
        {
            timer.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
            _logger.LogTrace("Debounce timer reset for: {Path}", fullPath);
        }
        else
        {
            var newTimer = new Timer(
                callback: _ => ProcessFileChange(libraryId, fullPath),
                state: null,
                dueTime: _debounceDelay,
                period: Timeout.InfiniteTimeSpan
            );

            if (!_debouncedEvents.TryAdd(fullPath, newTimer))
            {
                newTimer.Dispose();
            }
            else
            {
                _logger.LogTrace("Debounce timer started for: {Path}", fullPath);
            }
        }
    }

    private async void ProcessFileChange(int libraryId, string fullPath)
    {
        if (_debouncedEvents.TryRemove(fullPath, out var timer))
        {
            timer.Dispose();
        }

        _logger.LogInformation("处理防抖后的文件变更: LibraryId={LibraryId}, Path={Path}", libraryId, fullPath);

        using var scope = _serviceProvider.CreateScope();
        var libraryRepository = scope.ServiceProvider.GetRequiredService<IIndexLibraryRepository>();
        var library = await libraryRepository.GetByIdAsync(libraryId);

        if (library == null)
        {
            _logger.LogWarning("处理文件变更时找不到索引库: LibraryId={LibraryId}", libraryId);
            return;
        }

        var watchConfig = library.WatchConfigObject;
        if (!IsFileMatch(fullPath, watchConfig))
        {
            _logger.LogDebug("文件 {Path} 不匹配索引模式，已跳过。", fullPath);
            return;
        }

        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            await _backgroundTaskService.QueueFileUpdateTaskAsync(libraryId, fullPath);
        }
        else
        {
            await _backgroundTaskService.QueueFileDeleteTaskAsync(libraryId, fullPath);
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
        foreach (var timer in _debouncedEvents.Values)
        {
            timer.Dispose();
        }
        base.Dispose();
    }
}
