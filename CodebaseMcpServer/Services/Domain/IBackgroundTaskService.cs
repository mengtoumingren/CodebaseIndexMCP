using CodebaseMcpServer.Models.Domain;

namespace CodebaseMcpServer.Services.Domain
{
    /// <summary>
    /// 后台任务服务接口 - 负责将耗时的操作（如索引）排队
    /// </summary>
    public interface IBackgroundTaskService
    {
        /// <summary>
        /// 将索引任务排队等待执行
        /// </summary>
        /// <param name="libraryId">要索引的库ID</param>
        /// <param name="priority">任务优先级</param>
        /// <returns>表示任务的唯一ID</returns>
        Task<string> QueueIndexingTaskAsync(int libraryId, TaskPriority priority);

        /// <summary>
        /// 将文件更新任务排队
        /// </summary>
        Task<string> QueueFileUpdateTaskAsync(int libraryId, string filePath, TaskPriority priority = TaskPriority.High);

        /// <summary>
        /// 将文件删除任务排队
        /// </summary>
        Task<string> QueueFileDeleteTaskAsync(int libraryId, string filePath, TaskPriority priority = TaskPriority.High);

        /// <summary>
        /// 获取所有后台任务
        /// </summary>
        Task<List<BackgroundTask>> GetAllTasksAsync();

        /// <summary>
        /// 获取任务统计摘要
        /// </summary>
        Task<TaskSummaryDto> GetTaskSummaryAsync();
    }
}