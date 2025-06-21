# 实施计划：后台任务持久化与恢复

**目标:**
修改 `BackgroundTaskService`，使其在应用程序启动时，能够自动从数据库加载并重新排队处理那些在服务关闭前处于“待处理” (`Pending`) 或“运行中” (`Running`) 状态的任务。

**核心思路:**
1.  **服务启动时加载**: 在 `BackgroundTaskService` 的 `ExecuteAsync` 方法开始执行主循环之前，增加一个初始化步骤。
2.  **查询未完成任务**: 这个初始化步骤会查询数据库，获取所有状态为 `Pending` 和 `Running` 的任务。
3.  **重置中断任务**: 将所有 `Running` 状态的任务重置为 `Pending` 状态。这是因为服务重启意味着这些任务的执行已经被中断，需要重新开始。
4.  **重新排队**: 将所有待处理的任务（包括原始的 `Pending` 和被重置的 `Running` 任务）按照优先级和创建时间的顺序，重新加入到内存中的任务队列 (`_taskQueue`) 中，等待后续处理。

---

### 详细步骤

1.  **修改 `BackgroundTaskService.cs`**
    *   在 `ExecuteAsync` 方法的开头，也就是 `while` 循环之前，添加一个新的私有方法调用，例如 `await LoadAndRequeueUnfinishedTasksAsync(stoppingToken);`。

2.  **实现 `LoadAndRequeueUnfinishedTasksAsync` 方法**
    *   此方法将封装所有任务加载和重新排队的逻辑。
    *   **创建依赖注入作用域**: 由于 `BackgroundTaskService` 是一个长生命周期的单例服务，我们需要为这次数据库操作创建一个独立的 `IServiceScope` 来获取 `IBackgroundTaskRepository` 的实例。
    *   **获取任务**:
        *   调用 `repository.GetByStatusAsync(BackgroundTaskStatus.Running)` 获取所有上次正在运行的任务。
        *   调用 `repository.GetByStatusAsync(BackgroundTaskStatus.Pending)` 获取所有待处理的任务。
    *   **合并与处理**:
        *   将两个任务列表合并。
        *   遍历所有原先是 `Running` 状态的任务，将它们的 `Status` 属性更新为 `BackgroundTaskStatus.Pending`，并调用 `repository.UpdateAsync()` 保存更改。
    *   **排序**:
        *   对合并后的任务列表进行排序，优先处理高优先级的任务，对于相同优先级的任务，则先处理较早创建的。排序规则：`Priority` (降序), `CreatedAt` (升序)。
    *   **入队**:
        *   遍历排序后的任务列表，将每个任务的 `Id` 依次写入 `_taskQueue`。
        *   添加清晰的日志，记录加载了多少 `Pending` 和 `Running` 任务，以便于调试和监控。

---

### 流程图 (Mermaid)

```mermaid
graph TD
    A[BackgroundTaskService 启动] --> B(调用 ExecuteAsync);
    B --> C[执行新增的 LoadAndRequeueUnfinishedTasksAsync 方法];
    C --> D[创建 IServiceScope 和获取 Repository];
    D --> E[查询所有 'Running' 状态的任务];
    D --> F[查询所有 'Pending' 状态的任务];
    E --> G{遍历 'Running' 任务};
    G -- 有任务 --> H[更新状态为 'Pending' 并保存到数据库];
    H --> G;
    G -- 完成 --> I[合并 'Pending' 和已重置的 'Running' 任务];
    F --> I;
    I --> J[按优先级(高->低)和创建时间(早->晚)排序];
    J --> K{遍历排序后的任务列表};
    K -- 有任务 --> L[将任务ID添加到内存队列 _taskQueue];
    L --> K;
    K -- 完成 --> M[加载完成，记录日志];
    M --> N[进入主任务处理循环 (while !stoppingToken.IsCancellationRequested)];