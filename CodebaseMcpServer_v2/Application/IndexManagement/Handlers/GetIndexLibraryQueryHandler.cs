using System;
using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Application.Shared;
using CodebaseMcpServer.Application.IndexManagement.Queries;
using CodebaseMcpServer.Application.IndexManagement.DTOs;
using CodebaseMcpServer.Domain.IndexManagement.Repositories;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Application.IndexManagement.Handlers
{
    public class GetIndexLibraryQueryHandler : IQueryHandler<GetIndexLibraryQuery, IndexLibraryDto?>
    {
        private readonly IIndexLibraryRepository _repository;
        private readonly ILogger<GetIndexLibraryQueryHandler> _logger;
        
        public GetIndexLibraryQueryHandler(
            IIndexLibraryRepository repository,
            ILogger<GetIndexLibraryQueryHandler> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<IndexLibraryDto?> HandleAsync(GetIndexLibraryQuery query, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Handling GetIndexLibraryQuery for library: {LibraryId}", query.LibraryId);
            
            var library = await _repository.GetByIdAsync(new IndexLibraryId(query.LibraryId), cancellationToken);
            if (library == null)
            {
                _logger.LogWarning("Index library not found: {LibraryId}", query.LibraryId);
                return null;
            }
            
            var dto = new IndexLibraryDto(
                library.Id!.Value,
                library.CodebasePath!.Value,
                library.CollectionName!.Value,
                library.Status.ToString(),
                library.Statistics!.TotalFiles,
                library.Statistics!.IndexedFiles,
                library.Statistics!.TotalSize,
                library.CreatedAt,
                library.LastIndexedAt,
                library.WatchConfig?.IsEnabled ?? false
            );
            
            _logger.LogDebug("Successfully retrieved index library: {LibraryId}", query.LibraryId);
            return dto;
        }
    }
}