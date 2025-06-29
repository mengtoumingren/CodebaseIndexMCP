using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodebaseMcpServer.Application.Shared
{
    public interface INotificationService
    {
        Task NotifyIndexingStartedAsync(int libraryId, string codebasePath, CancellationToken cancellationToken = default);
        Task NotifyIndexingCompletedAsync(int libraryId, int totalFiles, int processedFiles, DateTime completedAt, CancellationToken cancellationToken = default);
        Task NotifyIndexingFailedAsync(int libraryId, string errorMessage, DateTime failedAt, CancellationToken cancellationToken = default);
        Task NotifyFileIndexUpdatedAsync(int libraryId, string filePath, DateTime updatedAt, CancellationToken cancellationToken = default);
        Task NotifyWatchingStatusChangedAsync(int libraryId, bool isEnabled, CancellationToken cancellationToken = default);
    }
}