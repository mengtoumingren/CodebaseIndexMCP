using System.Text.Json.Serialization;

namespace CodebaseMcpServer.Models.Domain;

/// <summary>
/// 后台任务实体 - 用于在数据库中跟踪长时间运行的操作
/// </summary>
public class BackgroundTask
{
    public int Id { get; set; }
    public string TaskId { get; set; } = Guid.NewGuid().ToString();
    public BackgroundTaskType Type { get; set; }
    public int? LibraryId { get; set; }
    public BackgroundTaskStatus Status { get; set; } = BackgroundTaskStatus.Pending;
    public int Progress { get; set; }
    public string? CurrentFile { get; set; }
    public string TaskConfig { get; set; } = "{}";
    public string TaskResult { get; set; } = "{}";
    public string? ErrorMessage { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
}

/// <summary>
/// 后台任务类型
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackgroundTaskType
{
    Indexing,
    Rebuilding,
    FileUpdate,
    WatcherRestart,
    SystemMaintenance
}

/// <summary>
/// 后台任务状态
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackgroundTaskStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// 任务优先级
/// </summary>
public enum TaskPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4
}