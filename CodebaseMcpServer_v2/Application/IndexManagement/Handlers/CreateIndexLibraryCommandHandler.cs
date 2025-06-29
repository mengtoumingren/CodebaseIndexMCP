using System;
using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Application.Shared;
using CodebaseMcpServer.Application.IndexManagement.Commands;
using CodebaseMcpServer.Application.IndexManagement.Services;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Application.IndexManagement.Handlers
{
    public class CreateIndexLibraryCommandHandler : ICommandHandler<CreateIndexLibraryCommand, Result<int>>
    {
        private readonly IndexLibraryApplicationService _applicationService;
        private readonly ILogger<CreateIndexLibraryCommandHandler> _logger;
        
        public CreateIndexLibraryCommandHandler(
            IndexLibraryApplicationService applicationService,
            ILogger<CreateIndexLibraryCommandHandler> logger)
        {
            _applicationService = applicationService ?? throw new ArgumentNullException(nameof(applicationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<Result<int>> HandleAsync(CreateIndexLibraryCommand command, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Handling CreateIndexLibraryCommand for path: {CodebasePath}", command.CodebasePath);
            
            var result = await _applicationService.HandleAsync(command, cancellationToken);
            
            if (result.IsSuccess)
            {
                _logger.LogInformation("Successfully created index library with ID: {LibraryId}", result.Value);
            }
            else
            {
                _logger.LogWarning("Failed to create index library: {Error}", result.Error);
            }
            
            return result;
        }
    }
}