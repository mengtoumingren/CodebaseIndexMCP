using System;
using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Application.Shared;
using CodebaseMcpServer.Application.IndexManagement.Commands;
using CodebaseMcpServer.Application.IndexManagement.Queries;
using CodebaseMcpServer.Application.IndexManagement.DTOs;
using CodebaseMcpServer.Domain.IndexManagement.Aggregates;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;
using CodebaseMcpServer.Domain.IndexManagement.Repositories;
using CodebaseMcpServer.Domain.IndexManagement.Services;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Application.IndexManagement.Services
{
    public class IndexLibraryApplicationService :
        ICommandHandler<CreateIndexLibraryCommand, Result<int>>,
        ICommandHandler<StartIndexingCommand, Result>,
        ICommandHandler<UpdateWatchConfigurationCommand, Result>,
        IQueryHandler<GetIndexLibraryQuery, IndexLibraryDto>,
        IQueryHandler<GetAllIndexLibrariesQuery, IReadOnlyList<IndexLibraryDto>>
    {
        private readonly IIndexLibraryRepository _repository;
        private readonly IProjectTypeDetectionService _projectDetectionService;
        private readonly IIndexingService _indexingService;
        private readonly IDomainEventDispatcher _eventDispatcher;
        private readonly ILogger<IndexLibraryApplicationService> _logger;
        
        public IndexLibraryApplicationService(
            IIndexLibraryRepository repository,
            IProjectTypeDetectionService projectDetectionService,
            IIndexingService indexingService,
            IDomainEventDispatcher eventDispatcher,
            ILogger<IndexLibraryApplicationService> logger)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _projectDetectionService = projectDetectionService ?? throw new ArgumentNullException(nameof(projectDetectionService));
            _indexingService = indexingService ?? throw new ArgumentNullException(nameof(indexingService));
            _eventDispatcher = eventDispatcher ?? throw new ArgumentNullException(nameof(eventDispatcher));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public async Task<Result<int>> HandleAsync(CreateIndexLibraryCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Creating index library for path: {CodebasePath}", command.CodebasePath);
                
                // 验证路径不存在重复
                var codebasePath = new CodebasePath(command.CodebasePath);
                var existingLibrary = await _repository.GetByCodebasePathAsync(codebasePath, cancellationToken);
                if (existingLibrary != null)
                {
                    return Result.Failure<int>("Codebase path already exists");
                }
                
                // 检测项目类型
                var projectType = await _projectDetectionService.DetectProjectTypeAsync(codebasePath, cancellationToken);
                
                // 创建聚合根
                var nextId = await _repository.GetNextIdAsync(cancellationToken);
                var library = new IndexLibrary(
                    new IndexLibraryId(nextId),
                    codebasePath,
                    new CollectionName(command.CollectionName)
                );
                
                // 保存到仓储
                await _repository.AddAsync(library, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
                
                // 分发领域事件
                await _eventDispatcher.DispatchAsync(library.DomainEvents, cancellationToken);
                library.ClearDomainEvents();
                
                _logger.LogInformation("Index library created successfully with ID: {LibraryId}", library.Id!.Value);
                return Result.Success(library.Id!.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create index library for path: {CodebasePath}", command.CodebasePath);
                return Result.Failure<int>(ex.Message);
            }
        }
        
        public async Task<Result> HandleAsync(StartIndexingCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Starting indexing for library: {LibraryId}", command.LibraryId);
                
                var library = await _repository.GetByIdAsync(new IndexLibraryId(command.LibraryId), cancellationToken);
                if (library == null)
                {
                    return Result.Failure("Index library not found");
                }
                
                // 启动索引
                library.StartIndexing();
                
                // 保存更改
                await _repository.UpdateAsync(library, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);
                
                // 分发领域事件
                await _eventDispatcher.DispatchAsync(library.DomainEvents, cancellationToken);
                library.ClearDomainEvents();
                
                // 异步执行索引任务
                _ = Task.Run(async () => await ExecuteIndexingAsync(library, cancellationToken), cancellationToken);
                
                _logger.LogInformation("Indexing started successfully for library: {LibraryId}", command.LibraryId);
                return Result.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start indexing for library: {LibraryId}", command.LibraryId);
                return Result.Failure(ex.Message);
            }
        }
        
        private async Task ExecuteIndexingAsync(IndexLibrary library, CancellationToken cancellationToken = default)
        {
            try
            {
                // 获取或创建默认索引配置
                var indexConfig = library.WatchConfig?.ToIndexConfiguration() ?? IndexConfiguration.Default;
                var result = await _indexingService.IndexCodebaseAsync(library.CodebasePath, indexConfig, cancellationToken);
                
                // 重新加载聚合根以避免并发问题
                var updatedLibrary = await _repository.GetByIdAsync(library.Id!, cancellationToken);
                if (updatedLibrary != null)
                {
                    updatedLibrary.CompleteIndexing(result);
                    await _repository.UpdateAsync(updatedLibrary);
                    await _repository.SaveChangesAsync();
                    
                    await _eventDispatcher.DispatchAsync(updatedLibrary.DomainEvents);
                    updatedLibrary.ClearDomainEvents();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Indexing failed for library: {LibraryId}", library.Id!.Value);
                
                var updatedLibrary = await _repository.GetByIdAsync(library.Id!, cancellationToken);
                if (updatedLibrary != null)
                {
                    updatedLibrary.FailIndexing(ex.Message);
                    await _repository.UpdateAsync(updatedLibrary);
                    await _repository.SaveChangesAsync();
                    
                    await _eventDispatcher.DispatchAsync(updatedLibrary.DomainEvents);
                    updatedLibrary.ClearDomainEvents();
                }
            }
        }
        
        public async Task<Result> HandleAsync(UpdateWatchConfigurationCommand command, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("Updating watch configuration for library: {LibraryId}", command.LibraryId);

                var library = await _repository.GetByIdAsync(new IndexLibraryId(command.LibraryId), cancellationToken);
                if (library == null)
                {
                    return Result.Failure("Index library not found");
                }

                var watchConfig = new WatchConfiguration(
                    command.IsEnabled,
                    command.IncludePatterns,
                    command.ExcludePatterns,
                    command.DebounceInterval
                );

                library.EnableWatching(watchConfig);

                await _repository.UpdateAsync(library, cancellationToken);
                await _repository.SaveChangesAsync(cancellationToken);

                await _eventDispatcher.DispatchAsync(library.DomainEvents, cancellationToken);
                library.ClearDomainEvents();

                _logger.LogInformation("Watch configuration updated successfully for library: {LibraryId}", command.LibraryId);
                return Result.Success();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update watch configuration for library: {LibraryId}", command.LibraryId);
                return Result.Failure(ex.Message);
            }
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
                library.WatchConfig?.IsEnabled ?? false // Handle null WatchConfig
            );

            _logger.LogDebug("Successfully retrieved index library: {LibraryId}", query.LibraryId);
            return dto;
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
                library.WatchConfig?.IsEnabled ?? false // Handle null WatchConfig
            )).ToList();

            _logger.LogDebug("Successfully retrieved {Count} index libraries", dtos.Count);
            return dtos;
        }
    }
}