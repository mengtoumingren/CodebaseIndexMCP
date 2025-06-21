# CodebaseApp 全新升级实施计划

## 🎯 升级概述

### 核心目标
1. **文件类型可配置化**：创建索引时可指定监听的文件类型和目录
2. **领域重新划分**：索引库服务、文件监视服务、后台任务服务三大核心模块
3. **SQLite数据存储**：替代现有JSON文件存储，提供事务性和并发安全
4. **Web管理看板**：提供可视化的配置管理和监控界面

### 技术架构升级
- **数据层**：JSON文件 → SQLite数据库
- **服务层**：单一服务 → 领域驱动的微服务架构  
- **接口层**：MCP only → MCP + REST API + Web UI
- **配置层**：静态配置 → 动态可配置

## 📋 第一阶段：数据存储层重构（2-3天）

### 1.1 SQLite数据库设计

#### 核心表结构：

```sql
-- 索引库配置表
CREATE TABLE IndexLibraries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name VARCHAR(100) NOT NULL,
    CodebasePath VARCHAR(500) NOT NULL UNIQUE,
    CollectionName VARCHAR(100) NOT NULL UNIQUE,
    Status VARCHAR(20) DEFAULT 'pending',
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    LastIndexedAt DATETIME,
    TotalFiles INTEGER DEFAULT 0,
    IndexedSnippets INTEGER DEFAULT 0,
    LastIndexingDuration INTEGER DEFAULT 0,
    IsActive BOOLEAN DEFAULT 1
);

-- 文件监控配置表
CREATE TABLE WatchConfigurations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    LibraryId INTEGER NOT NULL,
    FilePatterns TEXT NOT NULL, -- JSON数组：["*.cs", "*.ts"]
    ExcludePatterns TEXT NOT NULL, -- JSON数组：["bin", "obj", ".git"]
    IncludeSubdirectories BOOLEAN DEFAULT 1,
    IsEnabled BOOLEAN DEFAULT 1,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (LibraryId) REFERENCES IndexLibraries(Id) ON DELETE CASCADE
);

-- 文件索引详情表
CREATE TABLE FileIndexDetails (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    LibraryId INTEGER NOT NULL,
    RelativeFilePath VARCHAR(1000) NOT NULL,
    LastIndexedAt DATETIME NOT NULL,
    FileSize INTEGER,
    FileHash VARCHAR(64),
    SnippetCount INTEGER DEFAULT 0,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (LibraryId) REFERENCES IndexLibraries(Id) ON DELETE CASCADE,
    UNIQUE(LibraryId, RelativeFilePath)
);

-- 后台任务表
CREATE TABLE BackgroundTasks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TaskId VARCHAR(50) NOT NULL UNIQUE,
    Type VARCHAR(50) NOT NULL, -- 'indexing', 'rebuilding', 'file_update'
    LibraryId INTEGER,
    Status VARCHAR(20) DEFAULT 'pending', -- 'pending', 'running', 'completed', 'failed', 'cancelled'
    Progress INTEGER DEFAULT 0, -- 0-100
    CurrentFile VARCHAR(1000),
    ErrorMessage TEXT,
    StartedAt DATETIME,
    CompletedAt DATETIME,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (LibraryId) REFERENCES IndexLibraries(Id) ON DELETE SET NULL
);

-- 文件变更事件表（替代原有的文件存储）
CREATE TABLE FileChangeEvents (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    EventId VARCHAR(50) NOT NULL UNIQUE,
    LibraryId INTEGER NOT NULL,
    FilePath VARCHAR(1000) NOT NULL,
    ChangeType VARCHAR(20) NOT NULL, -- 'created', 'modified', 'deleted', 'renamed'
    Status VARCHAR(20) DEFAULT 'pending', -- 'pending', 'processing', 'completed', 'failed'
    ProcessedAt DATETIME,
    ErrorMessage TEXT,
    RetryCount INTEGER DEFAULT 0,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (LibraryId) REFERENCES IndexLibraries(Id) ON DELETE CASCADE
);

-- 系统配置表
CREATE TABLE SystemConfigurations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ConfigKey VARCHAR(100) NOT NULL UNIQUE,
    ConfigValue TEXT NOT NULL,
    ConfigType VARCHAR(20) DEFAULT 'string', -- 'string', 'number', 'boolean', 'json'
    Description TEXT,
    IsEditable BOOLEAN DEFAULT 1,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

### 1.2 数据访问层实现

```csharp
// 新增文件：Services/Data/DatabaseContext.cs
public class DatabaseContext
{
    private readonly IDbConnection _connection;
    private readonly ILogger<DatabaseContext> _logger;
    
    // 实现数据库连接、迁移、事务管理
}

// 新增文件：Services/Data/Repositories/
// - IIndexLibraryRepository.cs
// - IndexLibraryRepository.cs
// - IWatchConfigurationRepository.cs
// - WatchConfigurationRepository.cs
// - IFileIndexDetailRepository.cs
// - FileIndexDetailRepository.cs
// - IBackgroundTaskRepository.cs
// - BackgroundTaskRepository.cs
// - IFileChangeEventRepository.cs
// - FileChangeEventRepository.cs
```

### 1.3 数据迁移工具

```csharp
// 新增文件：Services/Migration/DataMigrationService.cs
public class DataMigrationService
{
    // 从现有JSON配置迁移到SQLite
    public async Task MigrateFromJsonConfigAsync()
    {
        // 1. 读取现有codebase-indexes.json
        // 2. 转换为新的数据库记录
        // 3. 迁移任务存储文件
        // 4. 备份原有配置
    }
}
```

## 📋 第二阶段：领域服务重构（3-4天）

### 2.1 索引库服务 (IndexLibraryService)

```csharp
// 新增文件：Services/Domain/IndexLibraryService.cs
public class IndexLibraryService : IIndexLibraryService
{
    // 职责：
    // - 索引库的CRUD操作
    // - 索引创建和重建逻辑
    // - 索引统计信息管理
    // - 集合管理（Qdrant）
    
    Task<IndexLibraryDto> CreateAsync(CreateIndexLibraryRequest request);
    Task<IndexLibraryDto> GetByIdAsync(int id);
    Task<IndexLibraryDto> GetByPathAsync(string path);
    Task<List<IndexLibraryDto>> GetAllAsync();
    Task<bool> DeleteAsync(int id);
    Task<IndexingResult> StartIndexingAsync(int libraryId);
    Task<IndexingResult> RebuildIndexAsync(int libraryId);
    Task<IndexStatistics> GetStatisticsAsync(int libraryId);
}
```

### 2.2 文件监视服务 (FileWatchService)

```csharp
// 重构现有：Services/FileWatchService.cs
public class FileWatchService : IFileWatchService
{
    // 职责：
    // - 基于配置的文件监控
    // - 可配置的文件类型过滤
    // - 文件变更事件管理
    // - 监控状态管理
    
    Task<bool> StartWatchingAsync(int libraryId);
    Task<bool> StopWatchingAsync(int libraryId);
    Task<WatchStatus> GetWatchStatusAsync(int libraryId);
    Task<bool> UpdateWatchConfigurationAsync(int libraryId, WatchConfigurationDto config);
    Task<List<FileChangeEvent>> GetPendingChangesAsync(int libraryId);
}
```

### 2.3 后台任务服务 (BackgroundTaskService)

```csharp
// 新增文件：Services/Domain/BackgroundTaskService.cs
public class BackgroundTaskService : BackgroundService, IBackgroundTaskService
{
    // 职责：
    // - 任务队列管理
    // - 任务执行调度
    // - 任务状态跟踪
    // - 并发控制
    
    Task<string> QueueIndexingTaskAsync(int libraryId, TaskPriority priority = TaskPriority.Normal);
    Task<string> QueueFileUpdateTaskAsync(int libraryId, string filePath);
    Task<BackgroundTaskDto> GetTaskStatusAsync(string taskId);
    Task<List<BackgroundTaskDto>> GetRunningTasksAsync();
    Task<bool> CancelTaskAsync(string taskId);
    Task<TaskStatistics> GetTaskStatisticsAsync();
}
```

## 📋 第三阶段：可配置文件类型支持（2天）

### 3.1 文件类型配置模型

```csharp
// 新增文件：Models/Configuration/FileTypeConfiguration.cs
public class FileTypeConfiguration
{
    public List<string> IncludePatterns { get; set; } = new();
    public List<string> ExcludePatterns { get; set; } = new();
    public List<string> ExcludeDirectories { get; set; } = new();
    public bool IncludeSubdirectories { get; set; } = true;
    public int MaxFileSize { get; set; } = 10 * 1024 * 1024; // 10MB
}

// 新增文件：Models/Configuration/ProjectTypePresets.cs
public static class ProjectTypePresets
{
    public static FileTypeConfiguration CSharpProject => new()
    {
        IncludePatterns = new() { "*.cs", "*.csx" },
        ExcludeDirectories = new() { "bin", "obj", ".vs", ".git" }
    };
    
    public static FileTypeConfiguration TypeScriptProject => new()
    {
        IncludePatterns = new() { "*.ts", "*.tsx", "*.js", "*.jsx" },
        ExcludeDirectories = new() { "node_modules", "dist", "build", ".git" }
    };
    
    public static FileTypeConfiguration PythonProject => new()
    {
        IncludePatterns = new() { "*.py", "*.pyi" },
        ExcludeDirectories = new() { "__pycache__", ".venv", "venv", ".git" }
    };
}
```

### 3.2 智能项目类型检测

```csharp
// 新增文件：Services/Analysis/ProjectTypeDetector.cs
public class ProjectTypeDetector
{
    public async Task<ProjectType> DetectProjectTypeAsync(string codebasePath)
    {
        // 基于特征文件检测项目类型
        // - *.csproj, *.sln → C#
        // - package.json, tsconfig.json → TypeScript/JavaScript
        // - requirements.txt, setup.py → Python
        // - Cargo.toml → Rust
        // - go.mod → Go
    }
    
    public FileTypeConfiguration GetRecommendedConfiguration(ProjectType type, string codebasePath)
    {
        // 返回推荐的文件类型配置
    }
}
```

## 📋 第四阶段：Web管理看板（3-4天）

### 4.1 REST API层

```csharp
// 新增文件：Controllers/IndexLibrariesController.cs
[ApiController]
[Route("api/[controller]")]
public class IndexLibrariesController : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<IndexLibraryDto>>> GetAllAsync();
    
    [HttpPost]
    public async Task<ActionResult<IndexLibraryDto>> CreateAsync(CreateIndexLibraryRequest request);
    
    [HttpGet("{id}")]
    public async Task<ActionResult<IndexLibraryDto>> GetByIdAsync(int id);
    
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteAsync(int id);
    
    [HttpPost("{id}/start-indexing")]
    public async Task<ActionResult<IndexingResult>> StartIndexingAsync(int id);
    
    [HttpPost("{id}/rebuild")]
    public async Task<ActionResult<IndexingResult>> RebuildAsync(int id);
    
    [HttpGet("{id}/statistics")]
    public async Task<ActionResult<IndexStatistics>> GetStatisticsAsync(int id);
}

// 类似的控制器：
// - WatchConfigurationsController.cs
// - BackgroundTasksController.cs  
// - SystemConfigurationsController.cs
// - DashboardController.cs
```

### 4.2 Web前端界面

```html
<!-- 新增目录：wwwroot/ -->
<!-- 
主要页面：
1. Dashboard - 系统概览和统计
2. Libraries - 索引库管理
3. Tasks - 任务监控
4. Configurations - 系统配置
5. Monitoring - 实时监控
-->

<!-- 技术栈：
- HTML5 + CSS3 + JavaScript (ES6+)
- 图表库：Chart.js
- UI框架：Bootstrap 5
- 实时通信：SignalR
-->
```

### 4.3 实时通信Hub

```csharp
// 新增文件：Hubs/MonitoringHub.cs
public class MonitoringHub : Hub
{
    // 实时推送：
    // - 任务进度更新
    // - 文件变更事件
    // - 系统状态变化
    // - 错误和警告通知
}
```

## 📋 第五阶段：MCP工具升级（1-2天）

### 5.1 升级现有MCP工具

```csharp
// 重构：Tools/IndexManagementTools.cs
public class IndexManagementTools
{
    [Tool("create_index_library_v2")]
    public async Task<string> CreateIndexLibraryV2(
        string codebasePath,
        string? name = null,
        string[]? filePatterns = null,
        string[]? excludePatterns = null,
        bool autoDetectType = true)
    {
        // 支持文件类型配置的创建索引
    }
    
    [Tool("update_watch_configuration")]
    public async Task<string> UpdateWatchConfiguration(
        string codebasePath,
        string[]? filePatterns = null,
        string[]? excludePatterns = null,
        bool? includeSubdirectories = null)
    {
        // 动态更新监控配置
    }
    
    [Tool("get_system_dashboard")]
    public async Task<string> GetSystemDashboard()
    {
        // 获取系统概览信息
    }
}
```

## 📋 第六阶段：测试和优化（2天）

### 6.1 单元测试

```csharp
// 新增目录：Tests/
// - Services/Domain/IndexLibraryServiceTests.cs
// - Services/Domain/FileWatchServiceTests.cs
// - Services/Domain/BackgroundTaskServiceTests.cs
// - Controllers/IndexLibrariesControllerTests.cs
```

### 6.2 集成测试

```csharp
// - Tests/Integration/DatabaseIntegrationTests.cs
// - Tests/Integration/MigrationIntegrationTests.cs
// - Tests/Integration/ApiIntegrationTests.cs
```

### 6.3 性能优化

- 数据库查询优化
- 并发处理优化
- 内存使用优化
- 文件监控性能优化

## 🚀 部署和迁移

### 部署步骤

1. **数据备份**：备份现有配置和任务数据
2. **数据库初始化**：创建SQLite数据库和表结构
3. **数据迁移**：运行迁移工具转换现有数据
4. **服务部署**：部署新版本服务
5. **配置验证**：验证所有配置正确迁移
6. **功能测试**：测试所有核心功能

### 回滚计划

- 保留原有JSON配置文件作为备份
- 提供迁移回滚工具
- 版本兼容性保证

## 📊 预期收益

### 功能增强
- ✅ 灵活的文件类型配置
- ✅ 可视化管理界面
- ✅ 实时监控和通知
- ✅ 更好的任务管理

### 性能提升
- ✅ SQLite事务性操作，提升并发安全
- ✅ 优化的数据查询性能
- ✅ 更好的内存管理
- ✅ 领域驱动的架构，降低耦合

### 可维护性
- ✅ 清晰的领域划分
- ✅ 标准化的数据访问层
- ✅ 完整的测试覆盖
- ✅ 现代化的Web界面

## ⏱️ 总体时间安排

- **第一阶段**：数据存储层重构 (2-3天)
- **第二阶段**：领域服务重构 (3-4天)  
- **第三阶段**：可配置文件类型 (2天)
- **第四阶段**：Web管理看板 (3-4天)
- **第五阶段**：MCP工具升级 (1-2天)
- **第六阶段**：测试和优化 (2天)

**总计预估时间：13-17天**

## 🔧 技术依赖

### 新增NuGet包
```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.0" />
<PackageReference Include="Dapper" Version="2.1.35" />
<PackageReference Include="Microsoft.AspNetCore.SignalR" Version="1.1.0" />
<PackageReference Include="FluentMigrator" Version="4.0.2" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="8.0.0" />
```

### 目录结构
```
CodebaseMcpServer/
├── Controllers/           # REST API控制器
├── Hubs/                 # SignalR通信中心
├── Services/
│   ├── Domain/           # 领域服务
│   ├── Data/             # 数据访问层
│   ├── Migration/        # 数据迁移
│   └── Analysis/         # 分析服务
├── Models/
│   ├── Domain/           # 领域模型
│   ├── Configuration/    # 配置模型
│   └── Api/              # API传输对象
├── wwwroot/              # Web前端资源
├── Migrations/           # 数据库迁移脚本
└── Tests/                # 测试项目