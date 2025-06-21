# CodebaseApp 数据库Schema优化方案

## 🎯 推荐方案：SQLite + JSON混合模式

### 核心表结构

```sql
-- 1. 索引库主表 (关系型数据)
CREATE TABLE IndexLibraries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(100) NOT NULL,
    CodebasePath VARCHAR(500) NOT NULL UNIQUE,
    CollectionName VARCHAR(100) NOT NULL UNIQUE,
    Status VARCHAR(20) DEFAULT 'pending',
    
    -- JSON列存储复杂/灵活数据
    WatchConfig JSON NOT NULL DEFAULT '{}',
    Statistics JSON NOT NULL DEFAULT '{}',
    Metadata JSON NOT NULL DEFAULT '{}',
    
    -- 基础时间字段
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    LastIndexedAt DATETIME,
    
    -- 关键指标(便于查询优化)
    TotalFiles INTEGER DEFAULT 0,
    IndexedSnippets INTEGER DEFAULT 0,
    IsActive BOOLEAN DEFAULT 1
);

-- 2. 文件索引详情表 (关系型，高频查询)
CREATE TABLE FileIndexDetails (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    LibraryId INTEGER NOT NULL,
    RelativeFilePath VARCHAR(1000) NOT NULL,
    LastIndexedAt DATETIME NOT NULL,
    FileSize INTEGER,
    FileHash VARCHAR(64),
    SnippetCount INTEGER DEFAULT 0,
    
    -- JSON列存储文件特定的元数据
    FileMetadata JSON DEFAULT '{}',
    
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (LibraryId) REFERENCES IndexLibraries(Id) ON DELETE CASCADE,
    UNIQUE(LibraryId, RelativeFilePath)
);

-- 3. 后台任务表 (关系型，状态查询频繁)
CREATE TABLE BackgroundTasks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId VARCHAR(50) NOT NULL UNIQUE,
    Type VARCHAR(50) NOT NULL,
    LibraryId INTEGER,
    Status VARCHAR(20) DEFAULT 'pending',
    Progress INTEGER DEFAULT 0,
    CurrentFile VARCHAR(1000),
    
    -- JSON列存储任务特定的配置和结果
    TaskConfig JSON DEFAULT '{}',
    TaskResult JSON DEFAULT '{}',
    
    ErrorMessage TEXT,
    StartedAt DATETIME,
    CompletedAt DATETIME,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (LibraryId) REFERENCES IndexLibraries(Id) ON DELETE SET NULL
);

-- 4. 文件变更事件表 (时序数据，可考虑分区)
CREATE TABLE FileChangeEvents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EventId VARCHAR(50) NOT NULL UNIQUE,
    LibraryId INTEGER NOT NULL,
    FilePath VARCHAR(1000) NOT NULL,
    ChangeType VARCHAR(20) NOT NULL,
    Status VARCHAR(20) DEFAULT 'pending',
    
    -- JSON列存储事件详情
    EventDetails JSON DEFAULT '{}',
    
    ProcessedAt DATETIME,
    ErrorMessage TEXT,
    RetryCount INTEGER DEFAULT 0,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (LibraryId) REFERENCES IndexLibraries(Id) ON DELETE CASCADE
);

-- 5. 系统配置表 (键值对 + JSON值)
CREATE TABLE SystemConfigurations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ConfigKey VARCHAR(100) NOT NULL UNIQUE,
    ConfigValue JSON NOT NULL,
    ConfigType VARCHAR(20) DEFAULT 'object',
    Description TEXT,
    IsEditable BOOLEAN DEFAULT 1,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

### JSON数据结构定义

```json
-- WatchConfig JSON结构
{
  "filePatterns": ["*.cs", "*.ts", "*.py"],
  "excludePatterns": ["bin", "obj", ".git", "node_modules"],
  "includeSubdirectories": true,
  "isEnabled": true,
  "maxFileSize": 10485760,
  "customFilters": [
    {
      "name": "exclude-test-files",
      "pattern": "**/*test*",
      "enabled": true
    }
  ]
}

-- Statistics JSON结构
{
  "indexedSnippets": 1250,
  "totalFiles": 45,
  "lastIndexingDuration": 125.5,
  "averageFileSize": 2048,
  "languageDistribution": {
    "csharp": 80,
    "typescript": 15,
    "json": 5
  },
  "indexingHistory": [
    {
      "date": "2025-06-21T10:00:00Z",
      "duration": 125.5,
      "filesProcessed": 45,
      "snippetsCreated": 1250
    }
  ]
}

-- Metadata JSON结构
{
  "projectType": "webapi",
  "framework": "net8.0",
  "team": "backend-team",
  "priority": "high",
  "tags": ["microservice", "authentication"],
  "customSettings": {
    "enableAdvancedParsing": true,
    "embeddingModel": "text-embedding-3-small"
  }
}
```

### 索引优化策略

```sql
-- 基础查询索引
CREATE INDEX idx_libraries_status ON IndexLibraries(Status);
CREATE INDEX idx_libraries_path ON IndexLibraries(CodebasePath);
CREATE INDEX idx_libraries_active ON IndexLibraries(IsActive, UpdatedAt);

-- JSON查询索引 (SQLite 3.45+)
CREATE INDEX idx_watch_enabled ON IndexLibraries(JSON_EXTRACT(WatchConfig, '$.isEnabled'));
CREATE INDEX idx_project_type ON IndexLibraries(JSON_EXTRACT(Metadata, '$.projectType'));

-- 文件详情查询索引
CREATE INDEX idx_files_library ON FileIndexDetails(LibraryId, LastIndexedAt);
CREATE INDEX idx_files_path ON FileIndexDetails(LibraryId, RelativeFilePath);

-- 任务查询索引
CREATE INDEX idx_tasks_status ON BackgroundTasks(Status, CreatedAt);
CREATE INDEX idx_tasks_library ON BackgroundTasks(LibraryId, Type);

-- 事件查询索引
CREATE INDEX idx_events_pending ON FileChangeEvents(Status, CreatedAt);
CREATE INDEX idx_events_library ON FileChangeEvents(LibraryId, CreatedAt);
```

## 🔧 数据访问层设计

### Repository接口优化

```csharp
public interface IIndexLibraryRepository
{
    // 基础CRUD
    Task<IndexLibrary> CreateAsync(IndexLibrary library);
    Task<IndexLibrary?> GetByIdAsync(int id);
    Task<IndexLibrary?> GetByPathAsync(string path);
    Task<List<IndexLibrary>> GetAllAsync();
    Task<bool> UpdateAsync(IndexLibrary library);
    Task<bool> DeleteAsync(int id);
    
    // JSON查询支持
    Task<List<IndexLibrary>> GetByProjectTypeAsync(string projectType);
    Task<List<IndexLibrary>> GetEnabledLibrariesAsync();
    Task<List<IndexLibrary>> SearchByMetadataAsync(string key, object value);
    
    // 统计查询
    Task<LibraryStatistics> GetStatisticsAsync();
    Task<Dictionary<string, int>> GetLanguageDistributionAsync();
    
    // 批量操作
    Task<bool> UpdateWatchConfigAsync(int libraryId, object watchConfig);
    Task<bool> UpdateStatisticsAsync(int libraryId, object statistics);
    Task<bool> AppendMetadataAsync(int libraryId, string key, object value);
}
```

### JSON操作辅助类

```csharp
public static class JsonQueryHelper
{
    public static string ExtractPath(string jsonColumn, string path)
    {
        return $"JSON_EXTRACT({jsonColumn}, '$.{path}')";
    }
    
    public static string ArrayLength(string jsonColumn, string arrayPath = "")
    {
        var path = string.IsNullOrEmpty(arrayPath) ? "" : $".{arrayPath}";
        return $"JSON_ARRAY_LENGTH({jsonColumn}, '${path}')";
    }
    
    public static string JsonSet(string jsonColumn, string path, object value)
    {
        return $"JSON_SET({jsonColumn}, '$.{path}', {FormatValue(value)})";
    }
    
    private static string FormatValue(object value)
    {
        return value switch
        {
            string s => $"'{s}'",
            bool b => b.ToString().ToLower(),
            null => "null",
            _ => value.ToString()
        };
    }
}

// 使用示例
public class SqliteIndexLibraryRepository : IIndexLibraryRepository
{
    public async Task<List<IndexLibrary>> GetEnabledLibrariesAsync()
    {
        var sql = $@"
            SELECT * FROM IndexLibraries 
            WHERE IsActive = 1 
            AND {JsonQueryHelper.ExtractPath("WatchConfig", "isEnabled")} = true
            ORDER BY UpdatedAt DESC";
            
        return await _connection.QueryAsync<IndexLibrary>(sql);
    }
    
    public async Task<bool> UpdateWatchConfigAsync(int libraryId, object watchConfig)
    {
        var sql = $@"
            UPDATE IndexLibraries 
            SET WatchConfig = @WatchConfig,
                UpdatedAt = CURRENT_TIMESTAMP
            WHERE Id = @LibraryId";
            
        var affected = await _connection.ExecuteAsync(sql, new { 
            LibraryId = libraryId,
            WatchConfig = JsonSerializer.Serialize(watchConfig)
        });
        
        return affected > 0;
    }
}
```

## 📊 性能优化建议

### 1. 查询优化

```sql
-- 高频查询优化
-- 获取活跃库的统计信息
SELECT 
    Id, Name,
    JSON_EXTRACT(Statistics, '$.indexedSnippets') as SnippetCount,
    JSON_EXTRACT(Statistics, '$.totalFiles') as FileCount,
    JSON_EXTRACT(WatchConfig, '$.isEnabled') as IsMonitored
FROM IndexLibraries 
WHERE IsActive = 1
ORDER BY UpdatedAt DESC;

-- 项目类型分组统计
SELECT 
    JSON_EXTRACT(Metadata, '$.projectType') as ProjectType,
    COUNT(*) as LibraryCount,
    SUM(CAST(JSON_EXTRACT(Statistics, '$.indexedSnippets') as INTEGER)) as TotalSnippets
FROM IndexLibraries 
WHERE IsActive = 1
GROUP BY JSON_EXTRACT(Metadata, '$.projectType');
```

### 2. 数据分区策略

```sql
-- 文件变更事件按月分区 (如果数据量大)
CREATE TABLE FileChangeEvents_202506 (
    CHECK (CreatedAt >= '2025-06-01' AND CreatedAt < '2025-07-01')
) INHERITS (FileChangeEvents);

-- 自动清理旧事件
DELETE FROM FileChangeEvents 
WHERE Status IN ('completed', 'failed') 
AND CreatedAt < datetime('now', '-30 days');
```

### 3. 缓存策略

```csharp
public class CachedIndexLibraryRepository : IIndexLibraryRepository
{
    private readonly IIndexLibraryRepository _baseRepository;
    private readonly IMemoryCache _cache;
    
    public async Task<IndexLibrary?> GetByPathAsync(string path)
    {
        var cacheKey = $"library:path:{path}";
        
        if (_cache.TryGetValue(cacheKey, out IndexLibrary? cached))
        {
            return cached;
        }
        
        var library = await _baseRepository.GetByPathAsync(path);
        if (library != null)
        {
            _cache.Set(cacheKey, library, TimeSpan.FromMinutes(10));
        }
        
        return library;
    }
}
```

## 🔄 数据迁移策略

```csharp
public class JsonMigrationService
{
    public async Task MigrateFromLegacyAsync()
    {
        // 1. 读取现有JSON配置
        var legacyConfig = await ReadLegacyConfigAsync();
        
        foreach (var mapping in legacyConfig.CodebaseMappings)
        {
            // 2. 转换为新的JSON格式
            var watchConfig = new
            {
                filePatterns = mapping.WatcherConfig.FileExtensions,
                excludePatterns = mapping.WatcherConfig.ExcludeDirectories,
                includeSubdirectories = mapping.WatcherConfig.IncludeSubdirectories,
                isEnabled = mapping.IsMonitoring
            };
            
            var statistics = new
            {
                indexedSnippets = mapping.Statistics.IndexedSnippets,
                totalFiles = mapping.Statistics.TotalFiles,
                lastIndexingDuration = ParseDuration(mapping.Statistics.LastIndexingDuration),
                lastUpdateTime = mapping.Statistics.LastUpdateTime
            };
            
            var metadata = new
            {
                friendlyName = mapping.FriendlyName,
                originalId = mapping.Id,
                migrationDate = DateTime.UtcNow
            };
            
            // 3. 插入到新表结构
            var library = new IndexLibrary
            {
                Name = mapping.FriendlyName,
                CodebasePath = mapping.CodebasePath,
                CollectionName = mapping.CollectionName,
                Status = MapStatus(mapping.IndexingStatus),
                WatchConfig = JsonSerializer.Serialize(watchConfig),
                Statistics = JsonSerializer.Serialize(statistics),
                Metadata = JsonSerializer.Serialize(metadata),
                CreatedAt = mapping.CreatedAt,
                LastIndexedAt = mapping.LastIndexed,
                TotalFiles = mapping.Statistics.TotalFiles,
                IndexedSnippets = mapping.Statistics.IndexedSnippets
            };
            
            await _repository.CreateAsync(library);
            
            // 4. 迁移文件索引详情
            await MigrateFileDetailsAsync(library.Id, mapping.FileIndexDetails);
        }
    }
}
```

这个混合方案既保持了SQLite的稳定性和成熟生态，又获得了NoSQL的灵活性，是CodebaseApp升级的最佳选择。