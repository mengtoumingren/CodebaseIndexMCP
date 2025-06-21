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
    }
}