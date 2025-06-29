using CodebaseMcpServer.Models.Domain;
using Dapper;

namespace CodebaseMcpServer.Services.Data.Repositories;

/// <summary>
/// 后台任务仓储实现
/// </summary>
public class BackgroundTaskRepository : IBackgroundTaskRepository
{
    private readonly DatabaseContext _context;
    private readonly ILogger<BackgroundTaskRepository> _logger;

    public BackgroundTaskRepository(DatabaseContext context, ILogger<BackgroundTaskRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<BackgroundTask> CreateAsync(BackgroundTask task)
    {
        var sql = @"
            INSERT INTO BackgroundTasks
            (TaskId, Type, LibraryId, Status, Progress, CurrentFile, FilePath, TaskConfig, TaskResult, ErrorMessage, StartedAt, CompletedAt, CreatedAt, UpdatedAt, Priority)
            VALUES
            (@TaskId, @Type, @LibraryId, @Status, @Progress, @CurrentFile, @FilePath, @TaskConfig, @TaskResult, @ErrorMessage, @StartedAt, @CompletedAt, @CreatedAt, @UpdatedAt, @Priority);
            SELECT last_insert_rowid();";

        var id = await _context.Connection.QuerySingleAsync<int>(sql, task);
        task.Id = id;
        _logger.LogInformation("创建后台任务: {TaskId} (ID: {Id})", task.TaskId, id);
        return task;
    }

    public async Task<BackgroundTask?> GetByIdAsync(int id)
    {
        var sql = "SELECT * FROM BackgroundTasks WHERE Id = @Id";
        return await _context.Connection.QueryFirstOrDefaultAsync<BackgroundTask>(sql, new { Id = id });
    }

    public async Task<BackgroundTask?> GetByTaskIdAsync(string taskId)
    {
        var sql = "SELECT * FROM BackgroundTasks WHERE TaskId = @TaskId";
        return await _context.Connection.QueryFirstOrDefaultAsync<BackgroundTask>(sql, new { TaskId = taskId });
    }

    public async Task<List<BackgroundTask>> GetByStatusAsync(BackgroundTaskStatus status)
    {
        var sql = "SELECT * FROM BackgroundTasks WHERE Status = @Status ORDER BY CreatedAt DESC";
        var results = await _context.Connection.QueryAsync<BackgroundTask>(sql, new { Status = status });
        return results.ToList();
    }

    public async Task<List<BackgroundTask>> GetPendingTasksAsync(int limit = 50)
    {
        var sql = @"
            SELECT * FROM BackgroundTasks 
            WHERE Status = 'Pending' 
            ORDER BY Priority DESC, CreatedAt ASC
            LIMIT @Limit";
        var results = await _context.Connection.QueryAsync<BackgroundTask>(sql, new { Limit = limit });
        return results.ToList();
    }

    public async Task<List<BackgroundTask>> GetRunningTasksByLibraryIdAsync(int libraryId)
    {
        var sql = "SELECT * FROM BackgroundTasks WHERE LibraryId = @LibraryId AND Status = 'Running'";
        var results = await _context.Connection.QueryAsync<BackgroundTask>(sql, new { LibraryId = libraryId });
        return results.ToList();
    }

    public async Task<bool> UpdateAsync(BackgroundTask task)
    {
        task.UpdatedAt = DateTime.UtcNow;
        var sql = @"
            UPDATE BackgroundTasks SET
                Type = @Type,
                LibraryId = @LibraryId,
                Status = @Status,
                Progress = @Progress,
                CurrentFile = @CurrentFile,
                FilePath = @FilePath,
                TaskConfig = @TaskConfig,
                TaskResult = @TaskResult,
                ErrorMessage = @ErrorMessage,
                StartedAt = @StartedAt,
                CompletedAt = @CompletedAt,
                UpdatedAt = @UpdatedAt,
                Priority = @Priority
            WHERE Id = @Id";
        
        var affected = await _context.Connection.ExecuteAsync(sql, task);
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var sql = "DELETE FROM BackgroundTasks WHERE Id = @Id";
        var affected = await _context.Connection.ExecuteAsync(sql, new { Id = id });
        return affected > 0;
    }

    public async Task<int> CleanupCompletedTasksAsync(TimeSpan maxAge)
    {
        var cutoffDate = DateTime.UtcNow.Subtract(maxAge);
        var sql = "DELETE FROM BackgroundTasks WHERE Status = 'Completed' AND CompletedAt < @CutoffDate";
        var affected = await _context.Connection.ExecuteAsync(sql, new { CutoffDate = cutoffDate });
        _logger.LogInformation("清理了 {Count} 个已完成的后台任务", affected);
        return affected;
    }

    public async Task<List<BackgroundTask>> GetAllAsync()
    {
        var sql = "SELECT * FROM BackgroundTasks ORDER BY CreatedAt DESC";
        var results = await _context.Connection.QueryAsync<BackgroundTask>(sql);
        return results.ToList();
    }
}