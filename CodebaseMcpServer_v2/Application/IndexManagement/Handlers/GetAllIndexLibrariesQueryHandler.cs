using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Application.Shared;
using CodebaseMcpServer.Application.IndexManagement.Queries;
using CodebaseMcpServer.Application.IndexManagement.DTOs;
using CodebaseMcpServer.Domain.IndexManagement.Repositories;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Application.IndexManagement.Handlers
{
    public class GetAllIndexLibrariesQueryHandler : IQueryHandler<GetAllIndexLibrariesQuery, IReadOnlyList<IndexLibraryDto>>
    {
        private readonly IIndexLibraryRepository _repository;
        private readonly ILogger<GetAllIndexLibrariesQueryHandler> _logger;
        
        public GetAllIndexLibrariesQueryHandler(
            IIndexLibraryRepository repository,
            ILogger<GetAllIndexLibrariesQueryHandler> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<IReadOnlyList<IndexLibraryDto>?> HandleAsync(GetAllIndexLibrariesQuery query, CancellationToken cancellationToken = default)
        {
            _logger.LogDebug("Handling GetAllIndexLibrariesQuery");
            
            var libraries = await _repository.GetAllAsync(cancellationToken);
            
            var dtos = libraries.Select(library => new IndexLibraryDto(
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
            )).ToList();
            
            _logger.LogDebug("Successfully retrieved {Count} index libraries", dtos.Count);
            return dtos;
        }
    }
}