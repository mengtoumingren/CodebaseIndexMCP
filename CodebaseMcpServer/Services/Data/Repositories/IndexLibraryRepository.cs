using CodebaseMcpServer.Models.Domain;
using Dapper;
using System.Text.Json;

namespace CodebaseMcpServer.Services.Data.Repositories;

/// <summary>
/// 索引库Repository实现 - SQLite + JSON支持
/// </summary>
public class IndexLibraryRepository : IIndexLibraryRepository
{
    private readonly DatabaseContext _context;
    private readonly ILogger<IndexLibraryRepository> _logger;

    public IndexLibraryRepository(DatabaseContext context, ILogger<IndexLibraryRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IndexLibrary> CreateAsync(IndexLibrary library)
    {
        var sql = @"
            INSERT INTO IndexLibraries 
            (Name, CodebasePath, CollectionName, Status, WatchConfig, Statistics, Metadata, 
             CreatedAt, UpdatedAt, LastIndexedAt, TotalFiles, IndexedSnippets, IsActive)
            VALUES 
            (@Name, @CodebasePath, @CollectionName, @Status, @WatchConfig, @Statistics, @Metadata,
             @CreatedAt, @UpdatedAt, @LastIndexedAt, @TotalFiles, @IndexedSnippets, @IsActive);
            
            SELECT last_insert_rowid();";

        var id = await _context.Connection.QuerySingleAsync<int>(sql, new
        {
            library.Name,
            library.CodebasePath,
            library.CollectionName,
            Status = library.Status.ToString(),
            library.WatchConfig,
            library.Statistics,
            library.Metadata,
            library.CreatedAt,
            library.UpdatedAt,
            library.LastIndexedAt,
            library.TotalFiles,
            library.IndexedSnippets,
            library.IsActive
        });
        
        library.Id = id;
        
        _logger.LogInformation("创建索引库: {Name} (ID: {Id})", library.Name, id);
        return library;
    }

    public async Task<IndexLibrary?> GetByIdAsync(int id)
    {
        var sql = @"
            SELECT * FROM IndexLibraries 
            WHERE Id = @Id AND IsActive = 1";
        
        var result = await _context.Connection.QueryFirstOrDefaultAsync<IndexLibrary>(sql, new { Id = id });
        if (result != null)
        {
            result.Status = Enum.Parse<IndexLibraryStatus>(await _context.Connection.QuerySingleAsync<string>(
                "SELECT Status FROM IndexLibraries WHERE Id = @Id", new { Id = id }));
        }
        return result;
    }

    public async Task<IndexLibrary?> GetByPathAsync(string codebasePath)
    {
        var sql = @"
            SELECT * FROM IndexLibraries 
            WHERE CodebasePath = @CodebasePath AND IsActive = 1";
        
        var result = await _context.Connection.QueryFirstOrDefaultAsync<IndexLibrary>(sql, 
            new { CodebasePath = codebasePath });
        
        if (result != null)
        {
            result.Status = Enum.Parse<IndexLibraryStatus>(await _context.Connection.QuerySingleAsync<string>(
                "SELECT Status FROM IndexLibraries WHERE Id = @Id", new { Id = result.Id }));
        }
        return result;
    }

    public async Task<IndexLibrary?> GetByCollectionNameAsync(string collectionName)
    {
        var sql = @"
            SELECT * FROM IndexLibraries 
            WHERE CollectionName = @CollectionName AND IsActive = 1";
        
        var result = await _context.Connection.QueryFirstOrDefaultAsync<IndexLibrary>(sql, 
            new { CollectionName = collectionName });
        
        if (result != null)
        {
            result.Status = Enum.Parse<IndexLibraryStatus>(await _context.Connection.QuerySingleAsync<string>(
                "SELECT Status FROM IndexLibraries WHERE Id = @Id", new { Id = result.Id }));
        }
        return result;
    }

    public async Task<List<IndexLibrary>> GetAllAsync()
    {
        var sql = @"
            SELECT * FROM IndexLibraries 
            WHERE IsActive = 1
            ORDER BY UpdatedAt DESC";
        
        var results = await _context.Connection.QueryAsync<IndexLibrary>(sql);
        var libraries = results.ToList();
        
        // 设置枚举状态
        foreach (var library in libraries)
        {
            library.Status = Enum.Parse<IndexLibraryStatus>(await _context.Connection.QuerySingleAsync<string>(
                "SELECT Status FROM IndexLibraries WHERE Id = @Id", new { Id = library.Id }));
        }
        
        return libraries;
    }

    public async Task<List<IndexLibrary>> GetEnabledLibrariesAsync()
    {
        var sql = $@"
            SELECT * FROM IndexLibraries 
            WHERE IsActive = 1 
            AND {JsonQueryHelper.Conditions.IsEnabled("WatchConfig")}
            ORDER BY UpdatedAt DESC";
            
        var results = await _context.Connection.QueryAsync<IndexLibrary>(sql);
        return results.ToList();
    }

    public async Task<List<IndexLibrary>> GetByProjectTypeAsync(string projectType)
    {
        var sql = $@"
            SELECT * FROM IndexLibraries 
            WHERE IsActive = 1 
            AND {JsonQueryHelper.Conditions.ProjectType("Metadata", projectType)}
            ORDER BY UpdatedAt DESC";
            
        var results = await _context.Connection.QueryAsync<IndexLibrary>(sql);
        return results.ToList();
    }

    public async Task<List<IndexLibrary>> GetByTeamAsync(string team)
    {
        var sql = $@"
            SELECT * FROM IndexLibraries 
            WHERE IsActive = 1 
            AND {JsonQueryHelper.Conditions.Team("Metadata", team)}
            ORDER BY UpdatedAt DESC";
            
        var results = await _context.Connection.QueryAsync<IndexLibrary>(sql);
        return results.ToList();
    }

    public async Task<List<IndexLibrary>> GetByStatusAsync(IndexLibraryStatus status)
    {
        var sql = @"
            SELECT * FROM IndexLibraries 
            WHERE IsActive = 1 AND Status = @Status
            ORDER BY UpdatedAt DESC";
            
        var results = await _context.Connection.QueryAsync<IndexLibrary>(sql, new { Status = status.ToString() });
        return results.ToList();
    }

    public async Task<List<IndexLibrary>> SearchByMetadataAsync(string key, object value)
    {
        var sql = $@"
            SELECT * FROM IndexLibraries 
            WHERE IsActive = 1 
            AND {JsonQueryHelper.ExtractPath("Metadata", key)} = @Value
            ORDER BY UpdatedAt DESC";
            
        var results = await _context.Connection.QueryAsync<IndexLibrary>(sql, new { Value = value.ToString() });
        return results.ToList();
    }

    public async Task<List<IndexLibrary>> GetByTagAsync(string tag)
    {
        var sql = $@"
            SELECT * FROM IndexLibraries 
            WHERE IsActive = 1 
            AND {JsonQueryHelper.Conditions.HasTag("Metadata", tag)}
            ORDER BY UpdatedAt DESC";
            
        var results = await _context.Connection.QueryAsync<IndexLibrary>(sql);
        return results.ToList();
    }

    public async Task<bool> UpdateAsync(IndexLibrary library)
    {
        var sql = @"
            UPDATE IndexLibraries 
            SET Name = @Name,
                CodebasePath = @CodebasePath,
                CollectionName = @CollectionName,
                Status = @Status,
                WatchConfig = @WatchConfig,
                Statistics = @Statistics,
                Metadata = @Metadata,
                UpdatedAt = @UpdatedAt,
                LastIndexedAt = @LastIndexedAt,
                TotalFiles = @TotalFiles,
                IndexedSnippets = @IndexedSnippets,
                IsActive = @IsActive
            WHERE Id = @Id";
            
        library.UpdatedAt = DateTime.UtcNow;
        
        var affected = await _context.Connection.ExecuteAsync(sql, new
        {
            library.Id,
            library.Name,
            library.CodebasePath,
            library.CollectionName,
            Status = library.Status.ToString(),
            library.WatchConfig,
            library.Statistics,
            library.Metadata,
            library.UpdatedAt,
            library.LastIndexedAt,
            library.TotalFiles,
            library.IndexedSnippets,
            library.IsActive
        });
        
        _logger.LogInformation("更新索引库: {Name} (ID: {Id})", library.Name, library.Id);
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var sql = @"
            UPDATE IndexLibraries 
            SET IsActive = 0, UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @Id";
            
        var affected = await _context.Connection.ExecuteAsync(sql, new { Id = id });
        
        _logger.LogInformation("删除索引库: ID={Id}", id);
        return affected > 0;
    }

    public async Task<bool> UpdateWatchConfigAsync(int libraryId, WatchConfigurationDto watchConfig)
    {
        var sql = @"
            UPDATE IndexLibraries 
            SET WatchConfig = @WatchConfig,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @LibraryId";
            
        var affected = await _context.Connection.ExecuteAsync(sql, new { 
            LibraryId = libraryId,
            WatchConfig = JsonSerializer.Serialize(watchConfig)
        });
        
        _logger.LogInformation("更新监控配置: LibraryId={LibraryId}", libraryId);
        return affected > 0;
    }

    public async Task<bool> UpdateStatisticsAsync(int libraryId, StatisticsDto statistics)
    {
        var sql = @"
            UPDATE IndexLibraries 
            SET Statistics = @Statistics,
                TotalFiles = @TotalFiles,
                IndexedSnippets = @IndexedSnippets,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @LibraryId";
            
        var affected = await _context.Connection.ExecuteAsync(sql, new { 
            LibraryId = libraryId,
            Statistics = JsonSerializer.Serialize(statistics),
            TotalFiles = statistics.TotalFiles,
            IndexedSnippets = statistics.IndexedSnippets
        });
        
        return affected > 0;
    }

    public async Task<bool> UpdateMetadataAsync(int libraryId, MetadataDto metadata)
    {
        var sql = @"
            UPDATE IndexLibraries 
            SET Metadata = @Metadata,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @LibraryId";
            
        var affected = await _context.Connection.ExecuteAsync(sql, new { 
            LibraryId = libraryId,
            Metadata = JsonSerializer.Serialize(metadata)
        });
        
        return affected > 0;
    }

    public async Task<bool> AppendMetadataAsync(int libraryId, string key, object value)
    {
        var sql = $@"
            UPDATE IndexLibraries 
            SET Metadata = {JsonQueryHelper.JsonSet("Metadata", key, value)},
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @LibraryId";
            
        var affected = await _context.Connection.ExecuteAsync(sql, new { LibraryId = libraryId });
        return affected > 0;
    }

    public async Task<bool> RemoveMetadataAsync(int libraryId, string key)
    {
        var sql = $@"
            UPDATE IndexLibraries 
            SET Metadata = {JsonQueryHelper.JsonRemove("Metadata", key)},
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @LibraryId";
            
        var affected = await _context.Connection.ExecuteAsync(sql, new { LibraryId = libraryId });
        return affected > 0;
    }

    public async Task<LibraryStatistics> GetStatisticsAsync()
    {
        var sql = $@"
            SELECT 
                COUNT(*) as TotalLibraries,
                SUM(TotalFiles) as TotalFiles,
                SUM(IndexedSnippets) as TotalSnippets,
                COUNT(CASE WHEN Status = 'Completed' THEN 1 END) as CompletedLibraries,
                COUNT(CASE WHEN Status = 'Failed' THEN 1 END) as FailedLibraries,
                COUNT(CASE WHEN {JsonQueryHelper.Conditions.IsEnabled("WatchConfig")} THEN 1 END) as MonitoredLibraries,
                COUNT(CASE WHEN IsActive = 1 THEN 1 END) as ActiveLibraries,
                AVG(CAST({JsonQueryHelper.ExtractPath("Statistics", "lastIndexingDuration")} AS REAL)) as AverageIndexingDuration
            FROM IndexLibraries 
            WHERE IsActive = 1";
        
        return await _context.Connection.QuerySingleAsync<LibraryStatistics>(sql);
    }

    public async Task<Dictionary<string, int>> GetLanguageDistributionAsync()
    {
        var sql = $@"
            SELECT 
                key as Language,
                CAST(value AS INTEGER) as Count
            FROM IndexLibraries,
                 {JsonQueryHelper.JsonEach("Statistics", "languageDistribution")}
            WHERE IsActive = 1";
        
        var results = await _context.Connection.QueryAsync<(string Language, int Count)>(sql);
        
        return results.GroupBy(r => r.Language)
                     .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
    }

    public async Task<Dictionary<string, int>> GetProjectTypeDistributionAsync()
    {
        var sql = $@"
            SELECT 
                {JsonQueryHelper.ExtractPath("Metadata", "projectType")} as ProjectType,
                COUNT(*) as Count
            FROM IndexLibraries 
            WHERE IsActive = 1
            GROUP BY {JsonQueryHelper.ExtractPath("Metadata", "projectType")}";
        
        var results = await _context.Connection.QueryAsync<(string ProjectType, int Count)>(sql);
        return results.ToDictionary(r => r.ProjectType ?? "unknown", r => r.Count);
    }

    public async Task<Dictionary<string, int>> GetTeamDistributionAsync()
    {
        var sql = $@"
            SELECT 
                {JsonQueryHelper.ExtractPath("Metadata", "team")} as Team,
                COUNT(*) as Count
            FROM IndexLibraries 
            WHERE IsActive = 1
            GROUP BY {JsonQueryHelper.ExtractPath("Metadata", "team")}";
        
        var results = await _context.Connection.QueryAsync<(string Team, int Count)>(sql);
        return results.ToDictionary(r => r.Team ?? "default", r => r.Count);
    }

    public async Task<bool> UpdateMultipleStatusAsync(List<int> libraryIds, IndexLibraryStatus status)
    {
        if (!libraryIds.Any()) return false;
        
        var sql = @"
            UPDATE IndexLibraries 
            SET Status = @Status, UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id IN @LibraryIds";
            
        var affected = await _context.Connection.ExecuteAsync(sql, new { 
            Status = status.ToString(),
            LibraryIds = libraryIds 
        });
        
        return affected > 0;
    }

    public async Task<List<IndexLibrary>> GetLibrariesForMonitoringAsync()
    {
        var sql = $@"
            SELECT * FROM IndexLibraries 
            WHERE IsActive = 1 
            AND Status IN ('Completed', 'Pending')
            AND {JsonQueryHelper.Conditions.IsEnabled("WatchConfig")}
            ORDER BY UpdatedAt DESC";
            
        var results = await _context.Connection.QueryAsync<IndexLibrary>(sql);
        return results.ToList();
    }
}