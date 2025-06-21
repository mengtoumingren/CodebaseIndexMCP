# 重构 FileWatcherService 计划

## 1. 目标

解除 `FileWatcherService` 对旧版 `IndexingTaskManager` 的依赖，使其完全融入新的基于 `IBackgroundTaskService` 的异步任务处理架构。最终目标是能够安全地删除 `IndexingTaskManager.cs` 和 `IndexConfigManager.cs`。

## 2. 现状分析

- **`IndexConfigManager.cs`**: 其核心功能（管理 `codebase-indexes.json`）已被 `IndexLibraryRepository` (SQLite) 取代。它目前仅通过 `IndexConfigManagerProxy` 和 `IndexConfigManagerAdapter` 作为兼容层存在，以支持 `IndexingTaskManager`。
- **`IndexingTaskManager.cs`**: 该类仍然被 `FileWatcherService` 积极使用，用于处理文件创建、修改和删除事件的增量索引。这是阻止其被删除的唯一关键依赖。
- **`FileWatcherService.cs`**: 当前直接注入并调用 `IndexingTaskManager` 的方法 (`UpdateFileIndexAsync`, `HandleFileDeletionAsync`) 来实时处理文件变更。
- **`IBackgroundTaskService`**: 新的后台任务服务已经能够处理批量的索引任务，但缺少处理单个文件增量更新的特定任务类型和逻辑。

## 3. 重构步骤

### 步骤 1: 扩展后台任务模型

为了处理文件级别的变更，我们需要定义新的任务类型和相应的参数。

**文件: `CodebaseMcpServer/Models/Domain/BackgroundTask.cs`**

1.  在 `BackgroundTaskType` 枚举中添加新的任务类型：
    ```csharp
    public enum BackgroundTaskType
    {
        Indexing,
        FileUpdate, // 新增
        FileDelete  // 新增
    }
    ```
2.  修改 `BackgroundTask` 实体，添加一个字段来存储与文件相关的路径信息。
    ```csharp
    public class BackgroundTask
    {
        // ... 其他属性
        public string? FilePath { get; set; } // 新增，用于存储变更的文件路径
    }
    ```
3.  更新 `IBackgroundTaskRepository` 和 `BackgroundTaskRepository` 以支持新字段的存取。

### 步骤 2: 扩展后台任务服务

**文件: `CodebaseMcpServer/Services/Domain/IBackgroundTaskService.cs`**

1.  添加新的任务排队接口：
    ```csharp
    public interface IBackgroundTaskService
    {
        Task<string> QueueIndexingTaskAsync(int libraryId, TaskPriority priority);
        Task<string> QueueFileUpdateTaskAsync(int libraryId, string filePath, TaskPriority priority = TaskPriority.High); // 新增
        Task<string> QueueFileDeleteTaskAsync(int libraryId, string filePath, TaskPriority priority = TaskPriority.High); // 新增
    }
    ```

**文件: `CodebaseMcpServer/Services/Domain/BackgroundTaskService.cs`**

1.  实现新的任务排队方法：
    ```csharp
    public async Task<string> QueueFileUpdateTaskAsync(int libraryId, string filePath, TaskPriority priority)
    {
        // ... 创建 BackgroundTask 实例，类型为 FileUpdate，并设置好 libraryId 和 filePath
        // ... 调用 repository.CreateAsync(task)
        // ... 将任务写入 _taskQueue
    }

    public async Task<string> QueueFileDeleteTaskAsync(int libraryId, string filePath, TaskPriority priority)
    {
        // ... 创建 BackgroundTask 实例，类型为 FileDelete，并设置好 libraryId 和 filePath
        // ... 调用 repository.CreateAsync(task)
        // ... 将任务写入 _taskQueue
    }
    ```
2.  在 `ProcessTaskAsync` 方法的 `switch` 语句中添加对新任务类型的处理逻辑：
    ```csharp
    private async Task<bool> ProcessTaskAsync(int taskId, CancellationToken stoppingToken)
    {
        // ...
        success = task.Type switch
        {
            BackgroundTaskType.Indexing => await ProcessIndexingTaskAsync(scope, task, stoppingToken),
            BackgroundTaskType.FileUpdate => await ProcessFileUpdateTaskAsync(scope, task, stoppingToken), // 新增
            BackgroundTaskType.FileDelete => await ProcessFileDeleteTaskAsync(scope, task, stoppingToken), // 新增
            _ => throw new NotSupportedException(...)
        };
        // ...
    }
    ```
3.  实现 `ProcessFileUpdateTaskAsync` 和 `ProcessFileDeleteTaskAsync` 方法：
    ```csharp
    private async Task<bool> ProcessFileUpdateTaskAsync(IServiceScope scope, BackgroundTask task, CancellationToken stoppingToken)
    {
        // 1. 获取 libraryRepository 和 searchService
        // 2. 根据 task.LibraryId 获取 IndexLibrary
        // 3. 调用 searchService.UpdateFileIndexAsync(task.FilePath, library.CollectionName)
        //    (这个方法需要从旧的 IndexingTaskManager 迁移或重新实现)
        // 4. 返回执行结果
    }

    private async Task<bool> ProcessFileDeleteTaskAsync(IServiceScope scope, BackgroundTask task, CancellationToken stoppingToken)
    {
        // 1. 获取 libraryRepository 和 searchService
        // 2. 根据 task.LibraryId 获取 IndexLibrary
        // 3. 调用 searchService.DeleteFileIndexAsync(task.FilePath, library.CollectionName)
        //    (这个方法需要从旧的 IndexingTaskManager 迁移或重新实现)
        // 4. 返回执行结果
    }
    ```
    *注意*: `UpdateFileIndexAsync` 和 `DeleteFileIndexAsync` 的逻辑需要从 `IndexingTaskManager` 迁移到 `EnhancedCodeSemanticSearch` 服务中，以保持职责单一。

### 步骤 3: 重构 `FileWatcherService`

**文件: `CodebaseMcpServer/Services/FileWatcherService.cs`**

1.  **修改构造函数**:
    *   移除 `IndexingTaskManager _indexingTaskManager;`
    *   注入 `IBackgroundTaskService _backgroundTaskService;`
2.  **修改事件处理器**:
    *   在 `OnFileChanged` 方法中，将 `await _indexingTaskManager.UpdateFileIndexAsync(...)` 替换为 `await _backgroundTaskService.QueueFileUpdateTaskAsync(libraryId, fullPath);`。
    *   在 `OnFileChanged` 方法中，将 `await _indexingTaskManager.HandleFileDeletionAsync(...)` 替换为 `await _backgroundTaskService.QueueFileDeleteTaskAsync(libraryId, fullPath);`。
    *   在 `OnFileRenamed` 方法中，同样进行替换。

### 步骤 4: 清理和删除

1.  **更新 `Program.cs`**:
    *   删除 `builder.Services.AddSingleton<IndexingTaskManager>();`。
    *   删除 `IndexConfigManagerProxy` 的注册和其定义，将 `IndexConfigManager` 的相关注册全部移除。
    *   删除 `IndexConfigManagerAdapter` 的注册。
2.  **删除文件**:
    *   确认项目可以成功编译。
    *   从项目中删除 `CodebaseMcpServer/Services/IndexingTaskManager.cs`。
    *   从项目中删除 `CodebaseMcpServer/Services/IndexConfigManager.cs`。
    *   从项目中删除 `CodebaseMcpServer/Services/Compatibility/IndexConfigManagerAdapter.cs`。
    *   删除旧的 `codebase-indexes.json` 和 `task-storage/` 目录（如果数据迁移已确认无误）。

## 4. 流程图

### 当前流程

```mermaid
graph TD
    A[File Change Event] --> B[FileWatcherService];
    B --> C{IndexingTaskManager};
    C --> D[Directly Updates Qdrant];
end
```

### 重构后流程

```mermaid
graph TD
    A2[File Change Event] --> E[FileWatcherService];
    E --> F{IBackgroundTaskService};
    F --> G[Adds Task to Queue];
    H[BackgroundTaskService Worker] --> I[Picks Task from Queue];
    I --> J[Updates Qdrant via SearchService];
end