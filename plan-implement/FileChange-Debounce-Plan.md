# 文件变化处理防抖机制实施计划

## 1. 目标

优化 `FileWatcherService`，为文件变化处理引入防抖（Debouncing）机制。这可以合并短时间内由频繁文件操作（如批量保存、Git操作）触发的连续事件，从而减少冗余任务，提高系统效率和响应能力。

## 2. 当前状态分析

*   **`FileWatcherService`**: 目前，该服务直接响应 `FileSystemWatcher` 的事件，并立即调用 `_backgroundTaskService` 将文件变更任务加入队列。
*   **核心问题**: 缺少防抖层，导致在文件活动频繁时产生大量不必要的处理任务，浪费系统资源。

## 3. 详细实施步骤

### 步骤 1: 引入防抖字典和配置

在 `FileWatcherService` 中添加一个并发字典来跟踪正在“抖动”的文件事件，并从配置中读取防抖延迟时间。

```csharp
// 用于跟踪和管理防抖事件的字典
private readonly ConcurrentDictionary<string, Timer> _debouncedEvents = new();
// 防抖延迟时间
private readonly TimeSpan _debounceDelay;

// 在构造函数中从配置初始化
public FileWatcherService(
    ILogger<FileWatcherService> logger,
    IServiceProvider serviceProvider,
    IBackgroundTaskService backgroundTaskService,
    IConfiguration configuration)
{
    // ... 其他代码 ...
    _debounceDelay = TimeSpan.FromMilliseconds(
        configuration.GetValue<int>("FileWatcher:DebounceDelayMilliseconds", 500)
    );
}
```

### 步骤 2: 修改事件处理程序

重构 `OnFileChanged` 和 `OnFileRenamed` 方法，使其调用新的防抖处理方法 `DebounceEvent`。

```csharp
// 原 OnFileChanged, OnFileRenamed 中的逻辑将被移至新的方法
watcher.Created += (s, e) => DebounceEvent(library.Id, e.FullPath, FileChangeType.Modified); // Created 和 Modified 视为同类
watcher.Changed += (s, e) => DebounceEvent(library.Id, e.FullPath, FileChangeType.Modified);
watcher.Deleted += (s, e) => DebounceEvent(library.Id, e.FullPath, FileChangeType.Deleted);
watcher.Renamed += (s, e) => {
    DebounceEvent(library.Id, e.OldFullPath, FileChangeType.Deleted);
    DebounceEvent(library.Id, e.FullPath, FileChangeType.Modified);
};
```

### 步骤 3: 实现防抖逻辑 (`DebounceEvent`)

此方法是防抖机制的核心。

```csharp
private void DebounceEvent(int libraryId, string fullPath, FileChangeType changeType)
{
    // 使用文件路径作为防抖的 Key
    var key = fullPath;

    if (_debouncedEvents.TryGetValue(key, out var timer))
    {
        // 如果已存在 Timer，则重置它（推迟执行）
        timer.Change(_debounceDelay, Timeout.InfiniteTimeSpan);
        _logger.LogTrace("Debounce timer reset for: {Path}", fullPath);
    }
    else
    {
        // 如果不存在，则创建新 Timer
        var newTimer = new Timer(
            callback: _ => ProcessFileChange(libraryId, fullPath, changeType),
            state: null,
            dueTime: _debounceDelay,
            period: Timeout.InfiniteTimeSpan
        );

        // 将新 Timer 添加到字典中
        if (!_debouncedEvents.TryAdd(key, newTimer))
        {
            // 如果添加失败（极小概率的并发场景），则销毁新创建的 Timer
            newTimer.Dispose();
        }
        else
        {
            _logger.LogTrace("Debounce timer started for: {Path}", fullPath);
        }
    }
}
```

### 步骤 4: 实现最终处理逻辑 (`ProcessFileChange`)

此方法由 `Timer` 回调触发，执行真正的文件处理任务。

```csharp
private async void ProcessFileChange(int libraryId, string fullPath, FileChangeType originalChangeType)
{
    // 从字典中移除 Timer
    if (_debouncedEvents.TryRemove(fullPath, out var timer))
    {
        timer.Dispose();
    }

    _logger.LogDebug("Processing debounced event for: {Path}", fullPath);

    // 检查文件是否符合被索引的模式 (复用现有逻辑)
    using var scope = _serviceProvider.CreateScope();
    var libraryRepository = scope.ServiceProvider.GetRequiredService<IIndexLibraryRepository>();
    var library = await libraryRepository.GetByIdAsync(libraryId);
    if (library == null || !IsFileMatch(fullPath, library.WatchConfigObject))
    {
        _logger.LogDebug("File {Path} does not match index patterns or library not found, skipping.", fullPath);
        return;
    }

    // 检查文件的最终状态来决定操作类型
    if (File.Exists(fullPath) || Directory.Exists(fullPath))
    {
        // 如果文件/目录存在，则视为更新/创建
        await _backgroundTaskService.QueueFileUpdateTaskAsync(libraryId, fullPath);
    }
    else
    {
        // 如果文件/目录不存在，则视为删除
        await _backgroundTaskService.QueueFileDeleteTaskAsync(libraryId, fullPath);
    }
}
```

### 步骤 5: 添加配置到 `appsettings.json`

```json
{
  "FileWatcher": {
    "DebounceDelayMilliseconds": 500
  }
}
```

## 4. 流程图

```mermaid
graph TD
    A[FileSystemWatcher 触发事件] --> B{DebounceEvent(filePath, changeType)};

    subgraph FileWatcherService
        B --> C{字典中是否存在 filePath?};
        C -- Yes --> D[重置现有 Timer];
        C -- No --> E[创建新 Timer 并存入字典];
        E --> F((Timer));
        D --> F;
        F -- 延迟后触发 --> G{ProcessFileChange(filePath)};
        G --> H[从字典中移除 Timer];
        H --> I[检查文件状态 (存在/不存在)];
        I -- 存在 --> J[QueueFileUpdateTaskAsync];
        I -- 不存在 --> K[QueueFileDeleteTaskAsync];
    end

    J --> L((BackgroundTaskService));
    K --> L;
```

## 5. 总结

该计划通过在 `FileWatcherService` 中引入一个可配置的防抖层，有效地解决了文件变更事件处理中的性能问题。它将合并短时间内的多个事件，确保只有一个任务被最终排队处理，从而显著提升系统的健壮性和效率。