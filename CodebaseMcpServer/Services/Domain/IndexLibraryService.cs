using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Data.Repositories;
using CodebaseMcpServer.Services.Analysis;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace CodebaseMcpServer.Services.Domain;

/// <summary>
/// 索引库服务实现 - 基于SQLite + JSON的索引库管理
/// </summary>
public class IndexLibraryService : IIndexLibraryService
{
    private readonly IIndexLibraryRepository _libraryRepository;
    private readonly ProjectTypeDetector _projectDetector;
    private readonly ILogger<IndexLibraryService> _logger;
    
    // 注入后台任务服务接口，稍后实现
    private readonly IBackgroundTaskService? _backgroundTaskService;

    public IndexLibraryService(
        IIndexLibraryRepository libraryRepository,
        ProjectTypeDetector projectDetector,
        ILogger<IndexLibraryService> logger,
        IBackgroundTaskService? backgroundTaskService = null)
    {
        _libraryRepository = libraryRepository;
        _projectDetector = projectDetector;
        _logger = logger;
        _backgroundTaskService = backgroundTaskService;
    }

    public async Task<CreateIndexLibraryResult> CreateAsync(CreateIndexLibraryRequest request)
    {
        try
        {
            _logger.LogInformation("创建索引库: {Path}", request.CodebasePath);
            
            // 1. 验证路径
            if (!Directory.Exists(request.CodebasePath))
            {
                return CreateIndexLibraryResult.CreateFailed("指定的路径不存在");
            }
            
            // 2. 检查是否已存在
            var existing = await _libraryRepository.GetByPathAsync(request.CodebasePath);
            if (existing != null)
            {
                return CreateIndexLibraryResult.CreateFailed("该路径已存在索引库");
            }
            
            // 3. 自动检测项目类型和配置
            var projectType = ProjectTypeDetector.ProjectType.Mixed;
            WatchConfigurationDto? recommendedWatchConfig = null;
            MetadataDto? recommendedMetadata = null;
            
            if (request.AutoDetectType)
            {
                var detectionResult = await _projectDetector.DetectProjectTypeAsync(request.CodebasePath);
                projectType = detectionResult.ProjectType;
                recommendedWatchConfig = _projectDetector.GetRecommendedWatchConfiguration(projectType, request.CodebasePath);
                recommendedMetadata = _projectDetector.GetRecommendedMetadata(projectType);
                
                _logger.LogInformation("检测到项目类型: {Type} (置信度: {Confidence:P0})", 
                    projectType, detectionResult.Confidence);
            }
            
            // 4. 构建JSON配置
            var watchConfig = new WatchConfigurationDto
            {
                FilePatterns = request.FilePatterns?.ToList() ?? 
                              recommendedWatchConfig?.FilePatterns ?? 
                              new List<string> { "*.cs" },
                ExcludePatterns = request.ExcludePatterns?.ToList() ?? 
                                 recommendedWatchConfig?.ExcludePatterns ?? 
                                 new List<string> { "bin", "obj", ".git" },
                IncludeSubdirectories = request.IncludeSubdirectories ?? true,
                IsEnabled = true,
                MaxFileSize = request.MaxFileSize ?? 10 * 1024 * 1024,
                CustomFilters = new List<CustomFilterDto>()
            };
            
            var statistics = new StatisticsDto
            {
                IndexedSnippets = 0,
                TotalFiles = 0,
                LastIndexingDuration = 0,
                AverageFileSize = 0,
                LanguageDistribution = new Dictionary<string, int>(),
                IndexingHistory = new List<IndexingHistoryDto>()
            };
            
            var metadata = new MetadataDto
            {
                ProjectType = projectType.ToString().ToLower(),
                Framework = recommendedMetadata?.Framework ?? "unknown",
                Team = request.Team ?? "default",
                Priority = request.Priority ?? "normal",
                Tags = request.Tags?.ToList() ?? new List<string>(),
                CustomSettings = new Dictionary<string, object>
                {
                    ["autoDetected"] = request.AutoDetectType,
                    ["createdVia"] = "api",
                    ["embeddingModel"] = recommendedMetadata?.CustomSettings?.GetValueOrDefault("embeddingModel") ?? "text-embedding-3-small"
                }
            };
            
            // 5. 创建索引库
            var library = new IndexLibrary
            {
                Name = request.Name ?? Path.GetFileName(request.CodebasePath.TrimEnd(Path.DirectorySeparatorChar)),
                CodebasePath = Path.GetFullPath(request.CodebasePath),
                CollectionName = GenerateCollectionName(request.CodebasePath),
                Status = IndexLibraryStatus.Pending,
                WatchConfig = JsonSerializer.Serialize(watchConfig),
                Statistics = JsonSerializer.Serialize(statistics),
                Metadata = JsonSerializer.Serialize(metadata)
            };
            
            library = await _libraryRepository.CreateAsync(library);
            
            // 6. 排队索引任务
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
        return await _libraryRepository.GetByPathAsync(path);
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

            // TODO: 这里应该清理Qdrant中的数据
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

        // 重建索引 - 清除现有统计信息
        var emptyStats = new StatisticsDto
        {
            IndexedSnippets = 0,
            TotalFiles = 0,
            LastIndexingDuration = 0,
            AverageFileSize = 0,
            LanguageDistribution = new Dictionary<string, int>(),
            IndexingHistory = library.StatisticsObject.IndexingHistory // 保留历史记录
        };
        
        await _libraryRepository.UpdateStatisticsAsync(libraryId, emptyStats);
        
        // 更新状态为pending
        library.Status = IndexLibraryStatus.Pending;
        await _libraryRepository.UpdateAsync(library);

        var taskId = await _backgroundTaskService.QueueIndexingTaskAsync(libraryId, TaskPriority.High);
        
        _logger.LogInformation("启动重建索引任务: LibraryId={LibraryId}, TaskId={TaskId}", libraryId, taskId);
        
        return taskId;
    }

    public async Task<bool> StopIndexingAsync(int libraryId)
    {
        if (_backgroundTaskService == null)
        {
            return false;
        }

        try
        {
            // TODO: 实现停止指定库的索引任务
            _logger.LogInformation("停止索引任务: LibraryId={LibraryId}", libraryId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止索引任务失败: LibraryId={LibraryId}", libraryId);
            return false;
        }
    }

    public async Task<bool> UpdateWatchConfigurationAsync(int libraryId, UpdateWatchConfigurationRequest request)
    {
        try
        {
            var library = await _libraryRepository.GetByIdAsync(libraryId);
            if (library == null)
            {
                return false;
            }
            
            // 解析现有JSON配置
            var currentConfig = library.WatchConfigObject;
            
            // 更新字段
            if (request.FilePatterns != null)
                currentConfig.FilePatterns = request.FilePatterns.ToList();
            
            if (request.ExcludePatterns != null)
                currentConfig.ExcludePatterns = request.ExcludePatterns.ToList();
            
            if (request.IncludeSubdirectories.HasValue)
                currentConfig.IncludeSubdirectories = request.IncludeSubdirectories.Value;
            
            if (request.IsEnabled.HasValue)
                currentConfig.IsEnabled = request.IsEnabled.Value;
            
            if (request.MaxFileSize.HasValue)
                currentConfig.MaxFileSize = request.MaxFileSize.Value;
            
            if (request.CustomFilters != null)
            {
                currentConfig.CustomFilters = request.CustomFilters.Select(cf => new CustomFilterDto
                {
                    Name = cf.Name,
                    Pattern = cf.Pattern,
                    Enabled = cf.Enabled
                }).ToList();
            }
            
            // 保存更新的JSON配置
            var success = await _libraryRepository.UpdateWatchConfigAsync(libraryId, currentConfig);
            
            if (success)
            {
                _logger.LogInformation("监控配置更新成功: LibraryId={LibraryId}", libraryId);
                
                // TODO: 通知文件监控服务重启监控
            }
            
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
            if (library == null)
            {
                return false;
            }
            
            var currentMetadata = library.MetadataObject;
            
            if (!string.IsNullOrEmpty(request.ProjectType))
                currentMetadata.ProjectType = request.ProjectType;
            
            if (!string.IsNullOrEmpty(request.Framework))
                currentMetadata.Framework = request.Framework;
            
            if (!string.IsNullOrEmpty(request.Team))
                currentMetadata.Team = request.Team;
            
            if (!string.IsNullOrEmpty(request.Priority))
                currentMetadata.Priority = request.Priority;
            
            if (request.Tags != null)
                currentMetadata.Tags = request.Tags.ToList();
            
            if (request.CustomSettings != null)
            {
                foreach (var setting in request.CustomSettings)
                {
                    currentMetadata.CustomSettings[setting.Key] = setting.Value;
                }
            }
            
            var success = await _libraryRepository.UpdateMetadataAsync(libraryId, currentMetadata);
            
            if (success)
            {
                _logger.LogInformation("元数据更新成功: LibraryId={LibraryId}", libraryId);
            }
            
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

    public async Task<IndexStatisticsDto> GetStatisticsAsync(int libraryId)
    {
        var library = await _libraryRepository.GetByIdAsync(libraryId);
        if (library == null)
            return null;
        
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
            TotalIndexedSnippets = repoStats.TotalSnippets,
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

    // 兼容性方法 - 用于现有MCP工具
    public async Task<CodebaseMapping?> GetLegacyMappingByPathAsync(string path)
    {
        var library = await _libraryRepository.GetByPathAsync(path);
        if (library == null)
            return null;

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

/// <summary>
/// 后台任务服务接口 - 暂时定义，稍后实现
/// </summary>
public interface IBackgroundTaskService
{
    Task<string> QueueIndexingTaskAsync(int libraryId, TaskPriority priority);
}