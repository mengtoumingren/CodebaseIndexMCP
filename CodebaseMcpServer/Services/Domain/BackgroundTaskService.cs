using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Data.Repositories;

namespace CodebaseMcpServer.Services.Domain
{
    /// <summary>
    /// 后台任务服务 - 适配器模式实现
    /// 将新的领域服务调用适配到旧的 IndexingTaskManager
    /// </summary>
    public class BackgroundTaskService : IBackgroundTaskService
    {
        private readonly ILogger<BackgroundTaskService> _logger;
        private readonly IndexingTaskManager _taskManager;
        private readonly IIndexLibraryRepository _libraryRepository;

        public BackgroundTaskService(
            ILogger<BackgroundTaskService> logger,
            IndexingTaskManager taskManager,
            IIndexLibraryRepository libraryRepository)
        {
            _logger = logger;
            _taskManager = taskManager;
            _libraryRepository = libraryRepository;
        }

        public async Task<string> QueueIndexingTaskAsync(int libraryId, TaskPriority priority)
        {
            var library = await _libraryRepository.GetByIdAsync(libraryId);
            if (library == null)
            {
                _logger.LogError("无法找到ID为 {LibraryId} 的索引库，无法排队任务", libraryId);
                throw new ArgumentException($"索引库不存在: {libraryId}");
            }

            _logger.LogInformation("接收到索引任务排队请求: LibraryId={LibraryId}, Path={Path}, Priority={Priority}",
                libraryId, library.CodebasePath, priority);

            // 根据优先级和任务类型调用不同的 IndexingTaskManager 方法
            // 注意：当前 IndexingTaskManager 没有区分优先级，这里为未来扩展预留
            // 我们将重建（High priority）和新建/更新（Normal/Low）都暂时映射到 RebuildIndexAsync
            // 因为 RebuildIndexAsync 包含了更完整的增量更新逻辑
            var result = await _taskManager.RebuildIndexAsync(library.CodebasePath);

            if (!result.Success)
            {
                _logger.LogError("通过 IndexingTaskManager 启动任务失败: {Message}", result.Message);
                throw new InvalidOperationException($"启动索引任务失败: {result.Message}");
            }

            _logger.LogInformation("任务已成功排队到 IndexingTaskManager: LibraryId={LibraryId}, TaskId={TaskId}",
                libraryId, result.TaskId);

            return result.TaskId;
        }
    }
}