using System.Text.Json.Serialization;

namespace CodebaseMcpServer.Models;

/// <summary>
/// 索引任务模型
/// </summary>
public class IndexingTask
{
    public string Id { get; set; } = string.Empty;
    public string CodebasePath { get; set; } = string.Empty;
    public IndexingStatus Status { get; set; } = IndexingStatus.Pending;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int IndexedCount { get; set; }
    public string? ErrorMessage { get; set; }
    public double ProgressPercentage { get; set; }
    public string? CurrentFile { get; set; }
}

/// <summary>
/// 索引任务状态
/// </summary>
public enum IndexingStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// 索引任务结果
/// </summary>
public class IndexingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? TaskId { get; set; }
    public int? IndexedCount { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? CollectionName { get; set; }
}

/// <summary>
/// 文件变更事件
/// </summary>
public class FileChangeEvent
{
    public string FilePath { get; set; } = string.Empty;
    public FileChangeType ChangeType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string CollectionName { get; set; } = string.Empty;
}

/// <summary>
/// 文件变更类型
/// </summary>
public enum FileChangeType
{
    Created,
    Modified,
    Deleted,
    Renamed
}