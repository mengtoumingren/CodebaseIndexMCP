using System;
using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Application.Shared;
using CodebaseMcpServer.Domain.IndexManagement.Events;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Application.IndexManagement.Handlers
{
    public class IndexingFailedEventHandler : IDomainEventHandler<IndexingFailed>
    {
        private readonly INotificationService _notificationService;
        private readonly ILogger<IndexingFailedEventHandler> _logger;
        
        public IndexingFailedEventHandler(
            INotificationService notificationService,
            ILogger<IndexingFailedEventHandler> logger)
        {
            _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task HandleAsync(IndexingFailed domainEvent, CancellationToken cancellationToken = default)
        {
            _logger.LogWarning("Handling IndexingFailed event for library {LibraryId}: {ErrorMessage}", 
                domainEvent.LibraryId.Value, domainEvent.ErrorMessage);
            
            try
            {
                await _notificationService.NotifyIndexingFailedAsync(
                    domainEvent.LibraryId.Value,
                    domainEvent.ErrorMessage,
                    domainEvent.OccurredOn,
                    cancellationToken
                );
                
                _logger.LogDebug("Successfully notified indexing failed for library {LibraryId}", domainEvent.LibraryId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to notify indexing failed for library {LibraryId}", domainEvent.LibraryId.Value);
            }
        }
    }
}