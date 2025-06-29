using System;
using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Application.Shared;
using CodebaseMcpServer.Application.IndexManagement.Commands;
using CodebaseMcpServer.Application.IndexManagement.Services;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Application.IndexManagement.Handlers
{
    public class StartIndexingCommandHandler : ICommandHandler<StartIndexingCommand, Result>
    {
        private readonly IndexLibraryApplicationService _applicationService;
        private readonly ILogger<StartIndexingCommandHandler> _logger;
        
        public StartIndexingCommandHandler(
            IndexLibraryApplicationService applicationService,
            ILogger<StartIndexingCommandHandler> logger)
        {
            _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<Result> HandleAsync(StartIndexingCommand command, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Handling StartIndexingCommand for library: {LibraryId}", command.LibraryId);
            
            var result = await _applicationService.HandleAsync(command, cancellationToken);
            
            if (result.IsSuccess)
            {
                _logger.LogInformation("Successfully started indexing for library: {LibraryId}", command.LibraryId);
            }
            else
            {
                _logger.LogWarning("Failed to start indexing for library {LibraryId}: {Error}", command.LibraryId, result.Error);
            }
            
            return result;
        }
    }
}