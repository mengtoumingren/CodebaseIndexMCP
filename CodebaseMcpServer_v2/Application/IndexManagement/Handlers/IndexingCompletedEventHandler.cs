using System;
using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Application.Shared;
using CodebaseMcpServer.Domain.IndexManagement.Events;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Application.IndexManagement.Handlers
{
    public class IndexingCompletedEventHandler : IDomainEventHandler<IndexingCompleted>
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<IndexingCompletedEventHandler> _logger;
        
        public IndexingCompletedEventHandler(
            INotificationService notificationService,
            ILogger<IndexingCompletedEventHandler> logger)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task HandleAsync(IndexingCompleted domainEvent, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Handling IndexingCompleted event for library {LibraryId}", domainEvent.LibraryId.Value);
            
            try
            {
                await _notificationService.NotifyIndexingCompletedAsync(
                    domainEvent.LibraryId.Value,
                    domainEvent.Result.TotalFiles,
                    domainEvent.Result.ProcessedFiles,
                    domainEvent.OccurredOn,
                    cancellationToken
                );
                
                _logger.LogDebug("Successfully notified indexing completed for library {LibraryId}", domainEvent.LibraryId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify indexing completed for library {LibraryId}", domainEvent.LibraryId.Value);
            }
        }
    }
}