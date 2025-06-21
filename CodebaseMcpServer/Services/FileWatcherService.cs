using System.Collections.Concurrent;
using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Data.Repositories;
using CodebaseMcpServer.Services.Domain;

namespace CodebaseMcpServer.Services;

/// <summary>
/// 文件监控服务 - 监控代码库文件变更并自动更新索引
/// </summary>
public class FileWatcherService : BackgroundService
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<int, FileSystemWatcher> _watchers = new();

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("文件监控服务启动");

        // 定期检查新的或更新的索引库
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshWatchersAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
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

    private void OnFileChanged(int libraryId, string fullPath, FileChangeType changeType)
    {
        // TODO: 实现将文件变更事件排队到后台任务服务的逻辑
        _logger.LogInformation("检测到文件变更: LibraryId={LibraryId}, Type={ChangeType}, Path={Path}", libraryId, changeType, fullPath);
    }

    private void OnFileRenamed(int libraryId, string oldFullPath, string newFullPath)
    {
        // TODO: 实现文件重命名事件的处理
        _logger.LogInformation("检测到文件重命名: LibraryId={LibraryId}, Old={OldPath}, New={NewPath}", libraryId, oldFullPath, newFullPath);
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

public enum FileChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed
}