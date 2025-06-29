using System;

namespace CodebaseMcpServer.Models
{
    public class FileChangeEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FilePath { get; set; } = string.Empty;
        public FileChangeType ChangeType { get; set; }
        public DateTime Timestamp { get; set; }
        public string CollectionName { get; set; } = string.Empty;
        public FileChangeStatus Status { get; set; } = FileChangeStatus.Pending;
        public DateTime? ProcessedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public int RetryCount { get; set; } = 0;
        public DateTime? LastRetryAt { get; set; }
    }

    public enum FileChangeType
    {
        Created,
        Modified,
        Deleted,
        Renamed // 虽然重命名会分解为删除+创建，但保留此类型可能有助于跟踪原始意图
    }

    public enum FileChangeStatus
    {
        Pending,    // 等待处理
        Processing, // 正在处理
        Completed,  // 处理完成
        Failed,     // 处理失败
        Expired     // 过期失效 (例如，超过最大重试次数或最大保留时间)
    }
}