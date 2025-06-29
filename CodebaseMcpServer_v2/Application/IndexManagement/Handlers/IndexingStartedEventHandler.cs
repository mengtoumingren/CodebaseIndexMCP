using System;
using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Application.Shared;
using CodebaseMcpServer.Domain.IndexManagement.Events;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Application.IndexManagement.Handlers
{
    public class IndexingStartedEventHandler : IDomainEventHandler<IndexingStarted>
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<IndexingStartedEventHandler> _logger;
        
        public IndexingStartedEventHandler(
            INotificationService notificationService,
            ILogger<IndexingStartedEventHandler> logger)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task HandleAsync(IndexingStarted domainEvent, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Handling IndexingStarted event for library {LibraryId}", domainEvent.LibraryId.Value);
            
            try
            {
                await _notificationService.NotifyIndexingStartedAsync(
                    domainEvent.LibraryId.Value,
                    domainEvent.CodebasePath.Value,
                    cancellationToken
                );
                
                _logger.LogDebug("Successfully notified indexing started for library {LibraryId}", domainEvent.LibraryId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify indexing started for library {LibraryId}", domainEvent.LibraryId.Value);
                // 不重新抛出异常，避免影响主流程
            }
        }
    }
}