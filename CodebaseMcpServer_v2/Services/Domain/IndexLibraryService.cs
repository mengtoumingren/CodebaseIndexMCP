using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Data.Repositories;
using CodebaseMcpServer.Services.Analysis;
using CodebaseMcpServer.Extensions;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using CodebaseMcpServer.Services.Configuration;

namespace CodebaseMcpServer.Services.Domain;

/// <summary>
/// 索引库服务实现 - 基于SQLite + JSON的索引库管理
/// </summary>
public class IndexLibraryService : IIndexLibraryService
{
    private readonly IIndexLibraryRepository _libraryRepository;
    private readonly ProjectTypeDetector _projectDetector;
    private readonly ILogger<IndexLibraryService> _logger;
    private readonly EnhancedCodeSemanticSearch _searchService;
    private readonly IBackgroundTaskService? _backgroundTaskService;
    private readonly IConfigurationPresetService _presetService;

    public IndexLibraryService(
        IIndexLibraryRepository libraryRepository,
        ProjectTypeDetector projectDetector,
        ILogger<IndexLibraryService> logger,
        EnhancedCodeSemanticSearch searchService,
        IConfigurationPresetService presetService,
        IBackgroundTaskService? backgroundTaskService = null)
    {
        _libraryRepository = libraryRepository;
        _projectDetector = projectDetector;
        _logger = logger;
        _searchService = searchService;
        _backgroundTaskService = backgroundTaskService;
        _presetService = presetService;
    }

    public async Task<CreateIndexLibraryResult> CreateAsync(CreateLibraryRequest request)
    {
        try
        {
            _logger.LogInformation("创建索引库: {Path}", request.CodebasePath);
            
            if (!Directory.Exists(request.CodebasePath))
            {
                return CreateIndexLibraryResult.CreateFailed("指定的路径不存在");
            }
            
            var existing = await _libraryRepository.GetByPathAsync(request.CodebasePath.NormalizePath());
            if (existing != null)
            {
                return CreateIndexLibraryResult.CreateFailed("该路径已存在索引库");
            }
            
            WatchConfigurationDto watchConfig;
            var projectType = "mixed";

            if (request.PresetIds != null && request.PresetIds.Any())
            {
                _logger.LogInformation("从 {Count} 个预设中合并配置", request.PresetIds.Count);
                watchConfig = await _presetService.MergePresetsAsync(request.PresetIds);
                // 在多预设模式下，项目类型可能需要更复杂的逻辑，暂时使用默认值
            }
            else
            {
                _logger.LogInformation("自动检测项目类型和配置");
                var detectionResult = await _projectDetector.DetectProjectTypeAsync(request.CodebasePath);
                projectType = detectionResult.ProjectType.ToString().ToLower();
                watchConfig = _projectDetector.GetRecommendedWatchConfiguration(detectionResult.ProjectType, request.CodebasePath);
                _logger.LogInformation("检测到项目类型: {Type} (置信度: {Confidence:P0})", projectType, detectionResult.Confidence);
            }
            
            var statistics = new StatisticsDto();
            var metadata = new MetadataDto
            {
                ProjectType = projectType,
                Team = "default",
                Priority = "normal"
            };
            
            var library = new IndexLibrary
            {
                Name = request.Name ?? Path.GetFileName(request.CodebasePath.TrimEnd(Path.DirectorySeparatorChar)),
                CodebasePath = request.CodebasePath.NormalizePath(),
                CollectionName = GenerateCollectionName(request.CodebasePath.NormalizePath()),
                Status = IndexLibraryStatus.Pending,
                WatchConfig = JsonSerializer.Serialize(watchConfig),
                Statistics = JsonSerializer.Serialize(statistics),
                Metadata = JsonSerializer.Serialize(metadata)
            };
            
            library = await _libraryRepository.CreateAsync(library);
            
            string? taskId = null;
            if (_backgroundTaskService != null)
            {
                try
                {
                    taskId = await _backgroundTaskService.QueueIndexingTaskAsync(library.Id, TaskPriority.Normal);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "排队索引任务失败，但索引库已创建: {LibraryId}", library.Id);
                }
            }
            
            _logger.LogInformation("索引库创建成功: {LibraryId}, 任务ID: {TaskId}", library.Id, taskId);
            
            return CreateIndexLibraryResult.CreateSuccess(library, taskId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建索引库失败: {Path}", request.CodebasePath);
            return CreateIndexLibraryResult.CreateFailed($"创建失败: {ex.Message}");
        }
    }

    public async Task<IndexLibrary?> GetByIdAsync(int id)
    {
        return await _libraryRepository.GetByIdAsync(id);
    }

    public async Task<IndexLibrary?> GetByPathAsync(string path)
    {
        return await _libraryRepository.GetByPathAsync(path.NormalizePath());
    }

    public async Task<IndexLibrary?> GetByCollectionNameAsync(string collectionName)
    {
        return await _libraryRepository.GetByCollectionNameAsync(collectionName);
    }

    public async Task<List<IndexLibrary>> GetAllAsync()
    {
        return await _libraryRepository.GetAllAsync();
    }

    public async Task<bool> UpdateAsync(IndexLibrary library)
    {
        return await _libraryRepository.UpdateAsync(library);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        try
        {
            var library = await _libraryRepository.GetByIdAsync(id);
            if (library == null)
            {
                return false;
            }

            await _searchService.DeleteCollectionAsync(library.CollectionName);
            
            _logger.LogInformation("删除索引库: {LibraryId} ({Name})", id, library.Name);
            
            return await _libraryRepository.DeleteAsync(id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除索引库失败: {LibraryId}", id);
            return false;
        }
    }

    public async Task<string> StartIndexingAsync(int libraryId, TaskPriority priority = TaskPriority.Normal)
    {
        if (_backgroundTaskService == null)
        {
            throw new InvalidOperationException("后台任务服务未配置");
        }

        var library = await _libraryRepository.GetByIdAsync(libraryId);
        if (library == null)
        {
            throw new ArgumentException($"索引库不存在: {libraryId}");
        }

        var taskId = await _backgroundTaskService.QueueIndexingTaskAsync(libraryId, priority);
        
        _logger.LogInformation("启动索引任务: LibraryId={LibraryId}, TaskId={TaskId}", libraryId, taskId);
        
        return taskId;
    }

    public async Task<string> RebuildIndexAsync(int libraryId)
    {
        if (_backgroundTaskService == null)
        {
            throw new InvalidOperationException("后台任务服务未配置");
        }

        var library = await _libraryRepository.GetByIdAsync(libraryId);
        if (library == null)
        {
            throw new ArgumentException($"索引库不存在: {libraryId}");
        }

        var emptyStats = new StatisticsDto
        {
            IndexingHistory = library.StatisticsObject.IndexingHistory
        };
        
        await _libraryRepository.UpdateStatisticsAsync(libraryId, emptyStats);
        
        library.Status = IndexLibraryStatus.Pending;
        await _libraryRepository.UpdateAsync(library);

        var taskId = await _backgroundTaskService.QueueIndexingTaskAsync(libraryId, TaskPriority.High);
        
        _logger.LogInformation("启动重建索引任务: LibraryId={LibraryId}, TaskId={TaskId}", libraryId, taskId);
        
        return taskId;
    }

    public Task<bool> StopIndexingAsync(int libraryId)
    {
        _logger.LogWarning("StopIndexingAsync 未实现");
        return Task.FromResult(false);
    }

    public async Task<bool> UpdateWatchConfigurationAsync(int libraryId, UpdateWatchConfigurationRequest request)
    {
        try
        {
            var library = await _libraryRepository.GetByIdAsync(libraryId);
            if (library == null) return false;
            
            var currentConfig = library.WatchConfigObject;
            
            if (request.FilePatterns != null) currentConfig.FilePatterns = request.FilePatterns.ToList();
            if (request.ExcludePatterns != null) currentConfig.ExcludePatterns = request.ExcludePatterns.ToList();
            if (request.IncludeSubdirectories.HasValue) currentConfig.IncludeSubdirectories = request.IncludeSubdirectories.Value;
            if (request.IsEnabled.HasValue) currentConfig.IsEnabled = request.IsEnabled.Value;
            if (request.MaxFileSize.HasValue) currentConfig.MaxFileSize = request.MaxFileSize.Value;
            
            if (request.CustomFilters != null)
            {
                currentConfig.CustomFilters = request.CustomFilters.Select(cf => new CustomFilterDto
                {
                    Name = cf.Name,
                    Pattern = cf.Pattern,
                    Enabled = cf.Enabled
                }).ToList();
            }
            
            var success = await _libraryRepository.UpdateWatchConfigAsync(libraryId, currentConfig);
            
            if (success) _logger.LogInformation("监控配置更新成功: LibraryId={LibraryId}", libraryId);
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新监控配置失败: LibraryId={LibraryId}", libraryId);
            return false;
        }
    }

    public async Task<bool> UpdateMetadataAsync(int libraryId, UpdateMetadataRequest request)
    {
        try
        {
            var library = await _libraryRepository.GetByIdAsync(libraryId);
            if (library == null) return false;
            
            var currentMetadata = library.MetadataObject;
            
            if (!string.IsNullOrEmpty(request.ProjectType)) currentMetadata.ProjectType = request.ProjectType;
            if (!string.IsNullOrEmpty(request.Framework)) currentMetadata.Framework = request.Framework;
            if (!string.IsNullOrEmpty(request.Team)) currentMetadata.Team = request.Team;
            if (!string.IsNullOrEmpty(request.Priority)) currentMetadata.Priority = request.Priority;
            if (request.Tags != null) currentMetadata.Tags = request.Tags.ToList();
            
            if (request.CustomSettings != null)
            {
                foreach (var setting in request.CustomSettings)
                {
                    currentMetadata.CustomSettings[setting.Key] = setting.Value;
                }
            }
            
            var success = await _libraryRepository.UpdateMetadataAsync(libraryId, currentMetadata);
            
            if (success) _logger.LogInformation("元数据更新成功: LibraryId={LibraryId}", libraryId);
            
            return success;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新元数据失败: LibraryId={LibraryId}", libraryId);
            return false;
        }
    }

    public async Task<List<IndexLibrary>> GetByProjectTypeAsync(string projectType)
    {
        return await _libraryRepository.GetByProjectTypeAsync(projectType);
    }

    public async Task<List<IndexLibrary>> GetByTeamAsync(string team)
    {
        return await _libraryRepository.GetByTeamAsync(team);
    }

    public async Task<List<IndexLibrary>> GetByStatusAsync(IndexLibraryStatus status)
    {
        return await _libraryRepository.GetByStatusAsync(status);
    }

    public async Task<List<IndexLibrary>> GetEnabledLibrariesAsync()
    {
        return await _libraryRepository.GetEnabledLibrariesAsync();
    }

    public async Task<List<IndexLibrary>> SearchByTagAsync(string tag)
    {
        return await _libraryRepository.GetByTagAsync(tag);
    }

    public async Task<IndexStatisticsDto?> GetStatisticsAsync(int libraryId)
    {
        var library = await _libraryRepository.GetByIdAsync(libraryId);
        if (library == null) return null;
        
        var stats = library.StatisticsObject;
        
        return new IndexStatisticsDto
        {
            LibraryId = libraryId,
            LibraryName = library.Name,
            Status = library.Status.ToString(),
            TotalFiles = library.TotalFiles,
            IndexedSnippets = library.IndexedSnippets,
            LastIndexingDuration = stats.LastIndexingDuration,
            AverageFileSize = stats.AverageFileSize,
            LanguageDistribution = stats.LanguageDistribution,
            IndexingHistory = stats.IndexingHistory,
            LastIndexedAt = library.LastIndexedAt,
            IsMonitored = library.WatchConfigObject.IsEnabled
        };
    }

    public async Task<CodebaseMcpServer.Models.Domain.LibraryStatistics> GetGlobalStatisticsAsync()
    {
        var repoStats = await _libraryRepository.GetStatisticsAsync();
        return new CodebaseMcpServer.Models.Domain.LibraryStatistics
        {
            TotalLibraries = repoStats.TotalLibraries,
            TotalSnippets = repoStats.TotalSnippets,
            TotalFiles = repoStats.TotalFiles,
            ActiveLibraries = repoStats.ActiveLibraries,
            LastCalculatedAt = repoStats.LastUpdated,
            AverageIndexingTime = repoStats.AverageIndexingDuration
        };
    }

    public async Task<Dictionary<string, int>> GetLanguageDistributionAsync()
    {
        return await _libraryRepository.GetLanguageDistributionAsync();
    }

    public async Task<Dictionary<string, int>> GetProjectTypeDistributionAsync()
    {
        return await _libraryRepository.GetProjectTypeDistributionAsync();
    }

    public async Task<bool> UpdateMultipleStatusAsync(List<int> libraryIds, IndexLibraryStatus status)
    {
        return await _libraryRepository.UpdateMultipleStatusAsync(libraryIds, status);
    }

    public async Task<List<IndexLibrary>> GetLibrariesForMonitoringAsync()
    {
        return await _libraryRepository.GetLibrariesForMonitoringAsync();
    }

    public async Task<CodebaseMapping?> GetLegacyMappingByPathAsync(string path)
    {
        var library = await _libraryRepository.GetByPathAsync(path.NormalizePath());
        if (library == null) return null;
        return ConvertToLegacyMapping(library);
    }

    public async Task<List<CodebaseMapping>> GetLegacyMappingsAsync()
    {
        var libraries = await _libraryRepository.GetAllAsync();
        return libraries.Select(ConvertToLegacyMapping).ToList();
    }

    private string GenerateCollectionName(string codebasePath)
    {
        var pathHash = SHA256.HashData(Encoding.UTF8.GetBytes(codebasePath));
        var hashString = Convert.ToHexString(pathHash)[..8].ToLower();
        return $"code_index_{hashString}";
    }

    private CodebaseMapping ConvertToLegacyMapping(IndexLibrary library)
    {
        var stats = library.StatisticsObject;
        
        return new CodebaseMapping
        {
            Id = library.Id.ToString(),
            FriendlyName = library.Name,
            CodebasePath = library.CodebasePath,
            NormalizedPath = library.CodebasePath,
            CollectionName = library.CollectionName,
            IndexingStatus = library.Status.ToString().ToLower(),
            IsMonitoring = library.WatchConfigObject.IsEnabled,
            CreatedAt = library.CreatedAt,
            LastIndexed = library.LastIndexedAt,
            Statistics = new IndexStatistics
            {
                TotalFiles = library.TotalFiles,
                IndexedSnippets = library.IndexedSnippets,
                LastIndexingDuration = $"{stats.LastIndexingDuration}s",
                LastUpdateTime = library.UpdatedAt
            }
        };
    }
}