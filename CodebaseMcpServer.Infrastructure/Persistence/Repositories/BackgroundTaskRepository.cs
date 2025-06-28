using CodebaseMcpServer.Domain.Entities;
using CodebaseMcpServer.Domain.Repositories;
using CodebaseMcpServer.Infrastructure.Persistence.Context;
using Dapper;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Infrastructure.Persistence.Repositories;

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

    public async Task AddAsync(BackgroundTask task)
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
    }

    public async Task<BackgroundTask?> GetByIdAsync(int id)
    {
        var sql = "SELECT * FROM BackgroundTasks WHERE Id = @Id";
        return await _context.Connection.QueryFirstOrDefaultAsync<BackgroundTask>(sql, new { Id = id });
    }

    public async Task<IEnumerable<BackgroundTask>> GetAllAsync()
    {
        var sql = "SELECT * FROM BackgroundTasks ORDER BY CreatedAt DESC";
        var results = await _context.Connection.QueryAsync<BackgroundTask>(sql);
        return results;
    }

    public async Task UpdateAsync(BackgroundTask task)
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
        
        await _context.Connection.ExecuteAsync(sql, task);
    }

    public async Task DeleteAsync(int id)
    {
        var sql = "DELETE FROM BackgroundTasks WHERE Id = @Id";
        await _context.Connection.ExecuteAsync(sql, new { Id = id });
    }
}
