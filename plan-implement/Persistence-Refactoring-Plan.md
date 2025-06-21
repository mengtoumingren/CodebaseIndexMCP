# 持久化层重构计划

**目标**: 将 `CodeSearchTools` 和 `IndexManagementTools` 从依赖旧的 `IndexConfigManager` 和 `IndexingTaskManager` 迁移到新的 `IIndexLibraryService` 和 `IBackgroundTaskService`。

---

## 1. 依赖注入更新

在 `Program.cs` 或服务初始化的地方，需要将 `IIndexLibraryService` 和 `IBackgroundTaskService` 的实例注入到 `CodeSearchTools` 和 `IndexManagementTools` 中。

**修改 `CodeSearchTools.cs` 和 `IndexManagementTools.cs`:**

*   移除 `_configManager` 和 `_taskManager` 静态字段。
*   添加 `_indexLibraryService` 和 `_backgroundTaskService` 静态字段。
*   更新 `Initialize` 方法以接收新服务的实例。

**示例 (`IndexManagementTools.cs`):**

```csharp
// 旧代码
private static IndexingTaskManager? _taskManager;
private static IndexConfigManager? _configManager;

public static void Initialize(IndexingTaskManager taskManager, IndexConfigManager configManager)
{
    _taskManager = taskManager;
    _configManager = configManager;
}

// 新代码
private static IIndexLibraryService? _indexLibraryService;
private static IBackgroundTaskService? _backgroundTaskService;

public static void Initialize(IIndexLibraryService indexLibraryService, IBackgroundTaskService backgroundTaskService)
{
    _indexLibraryService = indexLibraryService;
    _backgroundTaskService = backgroundTaskService;
}
```

---

## 2. `CodeSearchTools.cs` 重构

*   **`SemanticCodeSearch` 方法**:
    *   将 `_configManager.GetMappingByPathWithParentFallback(normalizedPath)` 替换为 `_indexLibraryService.GetLegacyMappingByPathAsync(normalizedPath)`。注意，新方法是异步的，需要使用 `await`。
    *   由于 `GetLegacyMappingByPathAsync` 可能不支持父目录回退逻辑，需要确认其实现。如果不支持，暂时简化逻辑，只按精确路径查找。
    *   更新对 `mapping` 对象的属性访问，以匹配 `CodebaseMapping` 类的定义。

---

## 3. `IndexManagementTools.cs` 重构

*   **`CreateIndexLibrary` 方法**:
    *   移除对 `_configManager.GetMappingByPath` 的调用。
    *   创建一个 `CreateIndexLibraryRequest` 对象，并填充 `codebasePath` 和 `friendlyName`。
    *   调用 `_indexLibraryService.CreateAsync(request)` 来创建索引库。
    *   根据返回的 `CreateIndexLibraryResult` 构建响应字符串。

*   **`GetIndexingStatus` 方法**:
    *   **按路径查询**:
        *   调用 `_indexLibraryService.GetByPathAsync(normalizedPath)` 获取索引库信息。
        *   如果找不到，则提示未建立索引。
        *   如果找到，则使用返回的 `IndexLibrary` 对象及其 `Statistics` 属性来填充响应。
    *   **按任务ID查询**:
        *   此功能现在由 `BackgroundTask` 模型处理。需要一个新的服务方法，如 `_backgroundTaskService.GetTaskByIdAsync(taskId)` (假设存在)。如果不存在，此功能可能需要调整或暂时移除。
    *   **查询所有**:
        *   调用 `_indexLibraryService.GetAllAsync()` 获取所有索引库。
        *   调用 `_indexLibraryService.GetGlobalStatisticsAsync()` 获取全局统计信息。
        *   组合这些信息来构建总览报告。

*   **`RebuildIndex` 方法**:
    *   调用 `_indexLibraryService.GetByPathAsync(codebasePath)` 获取索引库。
    *   如果找到，调用 `_indexLibraryService.RebuildIndexAsync(library.Id)`。
    *   根据结果返回成功或失败信息。

*   **`DeleteIndexLibrary` 方法**:
    *   调用 `_indexLibraryService.GetByPathAsync(normalizedPath)` 获取索引库。
    *   如果找到，并且 `confirm` 为 `true`，则调用 `_indexLibraryService.DeleteAsync(library.Id)`。
    *   如果 `confirm` 为 `false`，则构建一个需要用户确认的提示信息。

---

## 4. Mermaid 流程图

### `CreateIndexLibrary` 流程

```mermaid
graph TD
    A[Start: CreateIndexLibrary] --> B{Validate Path};
    B -->|Invalid| C[Return Error];
    B -->|Valid| D[Create CreateIndexLibraryRequest];
    D --> E{Call IIndexLibraryService.CreateAsync};
    E -->|Success| F[Get Result (Library + TaskId)];
    F --> G[Format Success Response];
    E -->|Failure| H[Get Error Message];
    H --> I[Format Failure Response];
    G --> Z[End];
    I --> Z[End];
    C --> Z[End];
```

---

## 5. 实施

完成计划评审后，切换到 `code` 模式，并按照上述步骤应用代码更改。