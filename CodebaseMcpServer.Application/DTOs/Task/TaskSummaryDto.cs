namespace CodebaseMcpServer.Application.DTOs.Task
{
    /// <summary>
    /// 任务统计摘要数据传输对象
    /// </summary>
    public class TaskSummaryDto
    {
        public int TotalTasks { get; set; }
        public int PendingTasks { get; set; }
        public int RunningTasks { get; set; }
        public int CompletedTasks { get; set; }
        public int FailedTasks { get; set; }
        public int CancelledTasks { get; set; }
    }
}
