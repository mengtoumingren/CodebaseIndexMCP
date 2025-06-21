# CodebaseApp 全新升级实施计划 (SQLite + JSON方案)

## 🎯 升级概述

### 核心目标
1. **文件类型可配置化**：创建索引时可指定监听的文件类型和目录
2. **领域重新划分**：索引库服务、文件监视服务、后台任务服务三大核心模块
3. **SQLite + JSON数据存储**：采用混合模式，关系型数据的稳定性 + JSON的灵活性
4. **Web管理看板**：提供可视化的配置管理和监控界面

### 技术架构升级
- **数据层**：JSON文件 → SQLite + JSON混合模式
- **服务层**：单一服务 → 领域驱动的微服务架构  
- **接口层**：MCP only → MCP + REST API + Web UI
- **配置层**：静态配置 → 动态可配置 + JSON灵活存储

## 📋 第一阶段：数据存储层重构（2-3天）

### 1.1 SQLite + JSON混合数据库设计

#### 核心表结构（关系型 + JSON列）：

```sql
-- 索引库主表 (混合模式)
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

-- 文件索引详情表 (关系型为主，JSON辅助)
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

-- 后台任务表 (关系型为主)
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

-- 文件变更事件表 (时序数据)
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

-- 系统配置表 (键值对 + JSON值)
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

#### JSON数据结构标准：

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

#### 索引优化策略：

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

### 1.2 数据访问层实现

```csharp
// 新增文件：Services/Data/DatabaseContext.cs
public class DatabaseContext : IDisposable
{
    private readonly IDbConnection _connection;
    private readonly ILogger<DatabaseContext> _logger;
    private IDbTransaction? _transaction;

    public DatabaseContext(IConfiguration configuration, ILogger<DatabaseContext> logger)
    {
        _logger = logger;
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Data Source=codebase-app.db";
        
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    public IDbConnection Connection => _connection;

    public async Task<IDbTransaction> BeginTransactionAsync()
    {
        _transaction = await _connection.BeginTransactionAsync();
        return _transaction;
    }

    // JSON操作辅助方法
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
    }
}

// 新增文件：Services/Data/Repositories/IndexLibraryRepository.cs
public class IndexLibraryRepository : IIndexLibraryRepository
{
    private readonly DatabaseContext _context;
    private readonly ILogger<IndexLibraryRepository> _logger;

    public async Task<IndexLibrary?> GetByPathAsync(string codebasePath)
    {
        var sql = @"
            SELECT * FROM IndexLibraries 
            WHERE CodebasePath = @CodebasePath AND IsActive = 1";
        
        return await _context.Connection.QueryFirstOrDefaultAsync<IndexLibrary>(sql, new { CodebasePath = codebasePath });
    }

    public async Task<List<IndexLibrary>> GetEnabledLibrariesAsync()
    {
        var sql = $@"
            SELECT * FROM IndexLibraries 
            WHERE IsActive = 1 
            AND {DatabaseContext.JsonQueryHelper.ExtractPath("WatchConfig", "isEnabled")} = true
            ORDER BY UpdatedAt DESC";
            
        return (await _context.Connection.QueryAsync<IndexLibrary>(sql)).ToList();
    }

    public async Task<bool> UpdateWatchConfigAsync(int libraryId, object watchConfig)
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
        
        return affected > 0;
    }
}
```

### 1.3 数据迁移工具

```csharp
// 新增文件：Services/Migration/JsonMigrationService.cs
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

## 📋 第二阶段：领域服务重构（3-4天）

### 2.1 索引库服务 (IndexLibraryService)

```csharp
// 重构：Services/Domain/IndexLibraryService.cs
public class IndexLibraryService : IIndexLibraryService
{
    public async Task<CreateIndexLibraryResult> CreateAsync(CreateIndexLibraryRequest request)
    {
        // 1. 自动检测项目类型和配置
        var projectType = await _projectDetector.DetectProjectTypeAsync(request.CodebasePath);
        var recommendedConfig = _projectDetector.GetRecommendedConfiguration(projectType, request.CodebasePath);
        
        // 2. 创建索引库
        var library = new IndexLibrary
        {
            Name = request.Name ?? Path.GetFileName(request.CodebasePath.TrimEnd(Path.DirectorySeparatorChar)),
            CodebasePath = Path.GetFullPath(request.CodebasePath),
            CollectionName = GenerateCollectionName(request.CodebasePath),
            Status = IndexLibraryStatus.Pending
        };
        
        // 3. 设置JSON配置
        var watchConfig = new
        {
            filePatterns = request.FilePatterns?.ToList() ?? recommendedConfig.IncludePatterns,
            excludePatterns = request.ExcludePatterns?.ToList() ?? recommendedConfig.ExcludeDirectories,
            includeSubdirectories = request.IncludeSubdirectories ?? true,
            isEnabled = true,
            maxFileSize = 10 * 1024 * 1024
        };
        
        var metadata = new
        {
            projectType = projectType.ToString().ToLower(),
            autoDetected = request.AutoDetectType,
            createdVia = "web_interface"
        };
        
        library.WatchConfig = JsonSerializer.Serialize(watchConfig);
        library.Metadata = JsonSerializer.Serialize(metadata);
        
        library = await _libraryRepository.CreateAsync(library);
        
        // 4. 排队索引任务
        var taskId = await _taskService.QueueIndexingTaskAsync(library.Id, TaskPriority.Normal);
        
        return CreateIndexLibraryResult.Success(library, taskId);
    }

    public async Task<bool> UpdateWatchConfigurationAsync(int libraryId, UpdateWatchConfigurationRequest request)
    {
        var library = await _libraryRepository.GetByIdAsync(libraryId);
        if (library == null) return false;
        
        // 解析现有JSON配置
        var currentConfig = JsonSerializer.Deserialize<Dictionary<string, object>>(library.WatchConfig);
        
        // 更新字段
        if (request.FilePatterns != null)
            currentConfig["filePatterns"] = request.FilePatterns.ToList();
        
        if (request.ExcludePatterns != null)
            currentConfig["excludePatterns"] = request.ExcludePatterns.ToList();
        
        if (request.IncludeSubdirectories.HasValue)
            currentConfig["includeSubdirectories"] = request.IncludeSubdirectories.Value;
        
        if (request.IsEnabled.HasValue)
            currentConfig["isEnabled"] = request.IsEnabled.Value;
        
        // 保存更新的JSON配置
        return await _libraryRepository.UpdateWatchConfigAsync(libraryId, currentConfig);
    }
}
```

### 2.2 文件监视服务重构

```csharp
// 重构：Services/FileWatchService.cs
public class FileWatchService : IFileWatchService
{
    public async Task<bool> StartWatchingAsync(int libraryId)
    {
        var library = await _libraryRepository.GetByIdAsync(libraryId);
        if (library == null) return false;
        
        // 解析JSON配置
        var watchConfig = JsonSerializer.Deserialize<WatchConfigurationDto>(library.WatchConfig);
        if (!watchConfig.IsEnabled) return false;
        
        // 创建文件监控器
        var watcher = new FileSystemWatcher(library.CodebasePath)
        {
            IncludeSubdirectories = watchConfig.IncludeSubdirectories,
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
        };
        
        // 动态设置文件过滤器
        foreach (var pattern in watchConfig.FilePatterns)
        {
            // 为每个模式创建单独的监控
            var patternWatcher = new FileSystemWatcher(library.CodebasePath, pattern)
            {
                IncludeSubdirectories = watchConfig.IncludeSubdirectories,
                EnableRaisingEvents = true
            };
            
            patternWatcher.Created += (s, e) => OnFileChanged(library, e, FileChangeType.Created, watchConfig);
            patternWatcher.Changed += (s, e) => OnFileChanged(library, e, FileChangeType.Modified, watchConfig);
            patternWatcher.Deleted += (s, e) => OnFileChanged(library, e, FileChangeType.Deleted, watchConfig);
            
            _watchers[$"{library.Id}_{pattern}"] = patternWatcher;
        }
        
        return true;
    }
    
    private void OnFileChanged(IndexLibrary library, FileSystemEventArgs e, FileChangeType changeType, WatchConfigurationDto config)
    {
        // 检查排除模式
        if (IsExcluded(e.FullPath, library.CodebasePath, config.ExcludePatterns))
            return;
        
        // 检查文件大小
        if (changeType != FileChangeType.Deleted && File.Exists(e.FullPath))
        {
            var fileInfo = new FileInfo(e.FullPath);
            if (fileInfo.Length > config.MaxFileSize)
            {
                _logger.LogDebug("文件 {Path} 超过大小限制 {MaxSize}", e.FullPath, config.MaxFileSize);
                return;
            }
        }
        
        // 创建变更事件并持久化
        var changeEvent = new FileChangeEvent
        {
            EventId = Guid.NewGuid().ToString(),
            LibraryId = library.Id,
            FilePath = e.FullPath,
            ChangeType = changeType,
            Status = FileChangeStatus.Pending,
            EventDetails = JsonSerializer.Serialize(new
            {
                fileSize = changeType != FileChangeType.Deleted ? new FileInfo(e.FullPath).Length : 0,
                detectedAt = DateTime.UtcNow,
                triggerPattern = GetMatchingPattern(e.FullPath, config.FilePatterns)
            })
        };
        
        _ = Task.Run(async () => await _eventRepository.CreateAsync(changeEvent));
    }
}

public class WatchConfigurationDto
{
    public List<string> FilePatterns { get; set; } = new();
    public List<string> ExcludePatterns { get; set; } = new();
    public bool IncludeSubdirectories { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public long MaxFileSize { get; set; } = 10 * 1024 * 1024;
}
```

## 📋 第三阶段：可配置文件类型支持（2天）

### 3.1 项目类型预设配置

```csharp
// 新增文件：Services/Analysis/ProjectTypeDetector.cs
public class ProjectTypeDetector
{
    public static readonly Dictionary<ProjectType, object> ProjectConfigurations = new()
    {
        [ProjectType.CSharp] = new
        {
            filePatterns = new[] { "*.cs", "*.csx", "*.cshtml", "*.razor" },
            excludePatterns = new[] { "bin", "obj", ".vs", ".git", "packages" },
            typicalFiles = new[] { "*.csproj", "*.sln", "Program.cs" },
            embeddingModel = "text-embedding-3-small"
        },
        [ProjectType.TypeScript] = new
        {
            filePatterns = new[] { "*.ts", "*.tsx", "*.js", "*.jsx" },
            excludePatterns = new[] { "node_modules", "dist", "build", ".git", "coverage" },
            typicalFiles = new[] { "package.json", "tsconfig.json", "webpack.config.js" },
            embeddingModel = "text-embedding-3-small"
        },
        [ProjectType.Python] = new
        {
            filePatterns = new[] { "*.py", "*.pyi", "*.pyx" },
            excludePatterns = new[] { "__pycache__", ".venv", "venv", ".git", "dist", "build" },
            typicalFiles = new[] { "requirements.txt", "setup.py", "pyproject.toml" },
            embeddingModel = "text-embedding-3-small"
        },
        [ProjectType.Mixed] = new
        {
            filePatterns = new[] { "*.cs", "*.ts", "*.js", "*.py", "*.java", "*.cpp", "*.h" },
            excludePatterns = new[] { "bin", "obj", "node_modules", "__pycache__", ".git", "dist", "build" },
            typicalFiles = new string[0],
            embeddingModel = "text-embedding-3-small"
        }
    };

    public async Task<ProjectType> DetectProjectTypeAsync(string codebasePath)
    {
        var detectedTypes = new List<ProjectType>();
        
        // 检测C#项目
        if (Directory.GetFiles(codebasePath, "*.csproj", SearchOption.AllDirectories).Any() ||
            Directory.GetFiles(codebasePath, "*.sln", SearchOption.AllDirectories).Any())
        {
            detectedTypes.Add(ProjectType.CSharp);
        }
        
        // 检测TypeScript/JavaScript项目
        if (File.Exists(Path.Combine(codebasePath, "package.json")) ||
            File.Exists(Path.Combine(codebasePath, "tsconfig.json")))
        {
            detectedTypes.Add(ProjectType.TypeScript);
        }
        
        // 检测Python项目
        if (File.Exists(Path.Combine(codebasePath, "requirements.txt")) ||
            File.Exists(Path.Combine(codebasePath, "setup.py")) ||
            File.Exists(Path.Combine(codebasePath, "pyproject.toml")))
        {
            detectedTypes.Add(ProjectType.Python);
        }
        
        // 返回检测结果
        return detectedTypes.Count switch
        {
            0 => ProjectType.Mixed, // 未识别，使用通用配置
            1 => detectedTypes[0],
            _ => ProjectType.Mixed  // 多种类型，使用混合配置
        };
    }
    
    public object GetRecommendedConfiguration(ProjectType projectType, string codebasePath)
    {
        return ProjectConfigurations.GetValueOrDefault(projectType, ProjectConfigurations[ProjectType.Mixed]);
    }
}
```

## 📋 第四阶段到第六阶段

第四阶段（Web管理看板）、第五阶段（MCP工具升级）和第六阶段（测试和优化）的实现保持不变，主要差异在于：

1. **数据访问层调用**：所有服务改为调用新的Repository接口
2. **JSON配置操作**：使用SQLite的JSON函数进行查询和更新
3. **配置管理**：通过JSON列存储灵活的配置数据

## 🔧 技术依赖更新

### 新增NuGet包
```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.0" />
<PackageReference Include="Dapper" Version="2.1.35" />
<PackageReference Include="System.Text.Json" Version="8.0.0" />
<PackageReference Include="Microsoft.AspNetCore.SignalR" Version="1.1.0" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
```

## 📊 SQLite + JSON方案的优势

1. **零学习成本** - 继续使用SQLite，团队熟悉
2. **关系型稳定性** - 核心数据使用关系型保证一致性
3. **JSON灵活性** - 配置和元数据使用JSON支持动态扩展
4. **强大查询能力** - SQLite 3.45+的JSON函数支持复杂查询
5. **渐进式升级** - 可以逐步将复杂数据迁移到JSON列
6. **工具生态** - 丰富的SQLite管理和调试工具

这个方案完美平衡了稳定性和灵活性，是CodebaseApp升级的最佳选择！