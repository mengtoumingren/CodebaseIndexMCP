using CodebaseMcpServer.Models.Domain;

namespace CodebaseMcpServer.Services.Data.Repositories;

/// <summary>
/// 索引库Repository接口
/// </summary>
public interface IIndexLibraryRepository
{
    // 基础CRUD
    Task<IndexLibrary> CreateAsync(IndexLibrary library);
    Task<IndexLibrary?> GetByIdAsync(int id);
    Task<IndexLibrary?> GetByPathAsync(string path);
    Task<IndexLibrary?> GetByCollectionNameAsync(string collectionName);
    Task<List<IndexLibrary>> GetAllAsync();
    Task<bool> UpdateAsync(IndexLibrary library);
    Task<bool> DeleteAsync(int id);
    
    // JSON查询支持
    Task<List<IndexLibrary>> GetByProjectTypeAsync(string projectType);
    Task<List<IndexLibrary>> GetEnabledLibrariesAsync();
    Task<List<IndexLibrary>> GetByTeamAsync(string team);
    Task<List<IndexLibrary>> GetByStatusAsync(IndexLibraryStatus status);
    Task<List<IndexLibrary>> SearchByMetadataAsync(string key, object value);
    Task<List<IndexLibrary>> GetByTagAsync(string tag);
    
    // 统计查询
    Task<LibraryStatistics> GetStatisticsAsync();
    Task<Dictionary<string, int>> GetLanguageDistributionAsync();
    Task<Dictionary<string, int>> GetProjectTypeDistributionAsync();
    Task<Dictionary<string, int>> GetTeamDistributionAsync();
    
    // JSON配置操作
    Task<bool> UpdateWatchConfigAsync(int libraryId, WatchConfigurationDto watchConfig);
    Task<bool> UpdateStatisticsAsync(int libraryId, StatisticsDto statistics);
    Task<bool> UpdateMetadataAsync(int libraryId, MetadataDto metadata);
    Task<bool> AppendMetadataAsync(int libraryId, string key, object value);
    Task<bool> RemoveMetadataAsync(int libraryId, string key);
    
    // 批量操作
    Task<bool> UpdateMultipleStatusAsync(List<int> libraryIds, IndexLibraryStatus status);
    Task<List<IndexLibrary>> GetLibrariesForMonitoringAsync();
}

/// <summary>
/// 库统计信息
/// </summary>
public class LibraryStatistics
{
    public int TotalLibraries { get; set; }
    public int TotalFiles { get; set; }
    public int TotalSnippets { get; set; }
    public int CompletedLibraries { get; set; }
    public int FailedLibraries { get; set; }
    public int MonitoredLibraries { get; set; }
    public int ActiveLibraries { get; set; }
    public double AverageIndexingDuration { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}