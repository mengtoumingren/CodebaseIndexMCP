using System.Threading.Tasks;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects; // For IndexLibraryId
using CodebaseMcpServer.Domain.TaskManagement.ValueObjects; // For TaskId, TaskPriority

namespace CodebaseMcpServer.Domain.TaskManagement.Services
{
    public interface ITaskSchedulingService
    {
        Task<TaskId> ScheduleIndexingTaskAsync(IndexLibraryId libraryId, TaskPriority priority);
        Task<bool> CanScheduleTaskAsync(IndexLibraryId libraryId);
    }
}