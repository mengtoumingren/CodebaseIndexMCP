using CodebaseMcpServer.Models.Domain;

namespace CodebaseMcpServer.Services.Domain;

/// <summary>
/// 索引库服务接口 - 基于SQLite + JSON的索引库管理
/// </summary>
public interface IIndexLibraryService
{
    // 基础索引库管理
    Task<CreateIndexLibraryResult> CreateAsync(CreateIndexLibraryRequest request);
    Task<IndexLibrary?> GetByIdAsync(int id);
    Task<IndexLibrary?> GetByPathAsync(string path);
    Task<IndexLibrary?> GetByCollectionNameAsync(string collectionName);
    Task<List<IndexLibrary>> GetAllAsync();
    Task<bool> UpdateAsync(IndexLibrary library);
    Task<bool> DeleteAsync(int id);
    
    // 索引操作
    Task<string> StartIndexingAsync(int libraryId, TaskPriority priority = TaskPriority.Normal);
    Task<string> RebuildIndexAsync(int libraryId);
    Task<bool> StopIndexingAsync(int libraryId);
    
    // 配置管理
    Task<bool> UpdateWatchConfigurationAsync(int libraryId, UpdateWatchConfigurationRequest request);
    Task<bool> UpdateMetadataAsync(int libraryId, UpdateMetadataRequest request);
    
    // 查询和搜索
    Task<List<IndexLibrary>> GetByProjectTypeAsync(string projectType);
    Task<List<IndexLibrary>> GetByTeamAsync(string team);
    Task<List<IndexLibrary>> GetByStatusAsync(IndexLibraryStatus status);
    Task<List<IndexLibrary>> GetEnabledLibrariesAsync();
    Task<List<IndexLibrary>> SearchByTagAsync(string tag);
    
    // 统计和报告
    Task<IndexStatisticsDto> GetStatisticsAsync(int libraryId);
    Task<CodebaseMcpServer.Models.Domain.LibraryStatistics> GetGlobalStatisticsAsync();
    Task<Dictionary<string, int>> GetLanguageDistributionAsync();
    Task<Dictionary<string, int>> GetProjectTypeDistributionAsync();
    
    // 批量操作
    Task<bool> UpdateMultipleStatusAsync(List<int> libraryIds, IndexLibraryStatus status);
    Task<List<IndexLibrary>> GetLibrariesForMonitoringAsync();
    
    // 兼容性方法（用于现有MCP工具）
    Task<CodebaseMapping?> GetLegacyMappingByPathAsync(string path);
    Task<List<CodebaseMapping>> GetLegacyMappingsAsync();
}

/// <summary>
/// 创建索引库请求
/// </summary>
public class CreateIndexLibraryRequest
{
    public string CodebasePath { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string[]? FilePatterns { get; set; }
    public string[]? ExcludePatterns { get; set; }
    public bool? IncludeSubdirectories { get; set; }
    public long? MaxFileSize { get; set; }
    public bool AutoDetectType { get; set; } = true;
    public string? Team { get; set; }
    public string? Priority { get; set; }
    public string[]? Tags { get; set; }
}

/// <summary>
/// 创建索引库结果
/// </summary>
public class CreateIndexLibraryResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public IndexLibrary? Library { get; set; }
    public string? TaskId { get; set; }

    public static CreateIndexLibraryResult CreateSuccess(IndexLibrary library, string? taskId)
    {
        return new CreateIndexLibraryResult { IsSuccess = true, Library = library, TaskId = taskId };
    }

    public static CreateIndexLibraryResult CreateFailed(string message)
    {
        return new CreateIndexLibraryResult { IsSuccess = false, Message = message };
    }
}

/// <summary>
/// 更新监控配置请求
/// </summary>
public class UpdateWatchConfigurationRequest
{
    public string[]? FilePatterns { get; set; }
    public string[]? ExcludePatterns { get; set; }
    public bool? IncludeSubdirectories { get; set; }
    public bool? IsEnabled { get; set; }
    public long? MaxFileSize { get; set; }
    public CustomFilterRequest[]? CustomFilters { get; set; }
}

/// <summary>
/// 更新元数据请求
/// </summary>
public class UpdateMetadataRequest
{
    public string? ProjectType { get; set; }
    public string? Framework { get; set; }
    public string? Team { get; set; }
    public string? Priority { get; set; }
    public string[]? Tags { get; set; }
    public Dictionary<string, object>? CustomSettings { get; set; }
}

/// <summary>
/// 自定义过滤器请求
/// </summary>
public class CustomFilterRequest
{
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 任务优先级
/// </summary>
public enum TaskPriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Critical = 4
}

/// <summary>
/// 索引统计DTO
/// </summary>
public class IndexStatisticsDto
{
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int TotalFiles { get; set; }
    public int IndexedSnippets { get; set; }
    public double LastIndexingDuration { get; set; }
    public long AverageFileSize { get; set; }
    public Dictionary<string, int> LanguageDistribution { get; set; } = new();
    public List<IndexingHistoryDto> IndexingHistory { get; set; } = new();
    public DateTime? LastIndexedAt { get; set; }
    public bool IsMonitored { get; set; }
}

/// <summary>
/// 兼容性 - 传统代码库映射
/// </summary>
public class CodebaseMapping
{
    public string Id { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string CodebasePath { get; set; } = string.Empty;
    public string NormalizedPath { get; set; } = string.Empty;
    public string CollectionName { get; set; } = string.Empty;
    public string IndexingStatus { get; set; } = string.Empty;
    public bool IsMonitoring { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastIndexed { get; set; }
    public IndexStatistics Statistics { get; set; } = new();
}

/// <summary>
/// 兼容性 - 索引统计
/// </summary>
public class IndexStatistics
{
    public int TotalFiles { get; set; }
    public int IndexedSnippets { get; set; }
    public string LastIndexingDuration { get; set; } = string.Empty;
    public DateTime? LastUpdateTime { get; set; }
}