using CodebaseMcpServer.Models.Domain;

namespace CodebaseMcpServer.Services.Data.Repositories;

/// <summary>
/// 后台任务仓储接口
/// </summary>
public interface IBackgroundTaskRepository
{
    Task<BackgroundTask> CreateAsync(BackgroundTask task);
    Task<BackgroundTask?> GetByIdAsync(int id);
    Task<BackgroundTask?> GetByTaskIdAsync(string taskId);
    Task<List<BackgroundTask>> GetByStatusAsync(BackgroundTaskStatus status);
    Task<List<BackgroundTask>> GetPendingTasksAsync(int limit = 50);
    Task<List<BackgroundTask>> GetRunningTasksByLibraryIdAsync(int libraryId);
    Task<bool> UpdateAsync(BackgroundTask task);
    Task<bool> DeleteAsync(int id);
    Task<int> CleanupCompletedTasksAsync(TimeSpan maxAge);
}