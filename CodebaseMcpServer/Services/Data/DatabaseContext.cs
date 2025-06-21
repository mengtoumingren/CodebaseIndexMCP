using Microsoft.Data.Sqlite;
using System.Data;
using System.Data.Common;
using Dapper;

namespace CodebaseMcpServer.Services.Data;

/// <summary>
/// SQLite + JSON 数据库上下文
/// </summary>
public class DatabaseContext : IDisposable
{
    private readonly IDbConnection _connection;
    private readonly ILogger<DatabaseContext> _logger;
    private DbTransaction? _transaction;

    public DatabaseContext(IConfiguration configuration, ILogger<DatabaseContext> logger)
    {
        _logger = logger;
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Data Source=codebase-app.db";
        
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        
        // 确保SQLite版本支持JSON函数
        EnsureJsonSupport();
        
        // 初始化数据库表结构
        InitializeDatabaseAsync().Wait();
    }

    public IDbConnection Connection => _connection;

    private void EnsureJsonSupport()
    {
        try
        {
            var version = _connection.QueryFirstOrDefault<string>("SELECT sqlite_version()");
            _logger.LogInformation("SQLite版本: {Version}", version);
            
            // 测试JSON支持
            var test = _connection.QueryFirstOrDefault<string>("SELECT JSON('{}')");
            _logger.LogInformation("JSON函数支持: 正常");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQLite JSON函数支持检查失败");
            throw new NotSupportedException("当前SQLite版本不支持JSON函数，请升级到3.45+");
        }
    }

    private async Task InitializeDatabaseAsync()
    {
        try
        {
            await CreateTablesAsync();
            await MigrateTablesAsync();
            await CreateIndexesAsync();
            _logger.LogInformation("数据库初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "数据库初始化失败");
            throw;
        }
    }

    private async Task CreateTablesAsync()
    {
        var createTableSql = @"
            -- 索引库主表 (混合模式)
            CREATE TABLE IF NOT EXISTS IndexLibraries (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name VARCHAR(100) NOT NULL,
                CodebasePath VARCHAR(500) NOT NULL UNIQUE,
                CollectionName VARCHAR(100) NOT NULL UNIQUE,
                Status VARCHAR(20) DEFAULT 'Pending',
                
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
            CREATE TABLE IF NOT EXISTS FileIndexDetails (
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
            CREATE TABLE IF NOT EXISTS BackgroundTasks (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TaskId VARCHAR(50) NOT NULL UNIQUE,
                Type VARCHAR(50) NOT NULL,
                LibraryId INTEGER,
                Status VARCHAR(20) DEFAULT 'Pending',
                Progress INTEGER DEFAULT 0,
                CurrentFile VARCHAR(1000),
                FilePath VARCHAR(1000),
                
                -- JSON列存储任务特定的配置和结果
                TaskConfig JSON DEFAULT '{}',
                TaskResult JSON DEFAULT '{}',
                
                ErrorMessage TEXT,
                StartedAt DATETIME,
                CompletedAt DATETIME,
                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                Priority INTEGER DEFAULT 2,
                
                FOREIGN KEY (LibraryId) REFERENCES IndexLibraries(Id) ON DELETE SET NULL
            );

            -- 文件变更事件表 (时序数据)
            CREATE TABLE IF NOT EXISTS FileChangeEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventId VARCHAR(50) NOT NULL UNIQUE,
                LibraryId INTEGER NOT NULL,
                FilePath VARCHAR(1000) NOT NULL,
                ChangeType VARCHAR(20) NOT NULL,
                Status VARCHAR(20) DEFAULT 'Pending',
                
                -- JSON列存储事件详情
                EventDetails JSON DEFAULT '{}',
                
                ProcessedAt DATETIME,
                ErrorMessage TEXT,
                RetryCount INTEGER DEFAULT 0,
                LastRetryAt DATETIME,
                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                
                FOREIGN KEY (LibraryId) REFERENCES IndexLibraries(Id) ON DELETE CASCADE
            );

            -- 系统配置表 (键值对 + JSON值)
            CREATE TABLE IF NOT EXISTS SystemConfigurations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ConfigKey VARCHAR(100) NOT NULL UNIQUE,
                ConfigValue JSON NOT NULL,
                ConfigType VARCHAR(20) DEFAULT 'object',
                Description TEXT,
                IsEditable BOOLEAN DEFAULT 1,
                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
            );
        ";

        await _connection.ExecuteAsync(createTableSql);
    }

    private async Task CreateIndexesAsync()
    {
        var createIndexesSql = @"
            -- 基础查询索引
            CREATE INDEX IF NOT EXISTS idx_libraries_status ON IndexLibraries(Status);
            CREATE INDEX IF NOT EXISTS idx_libraries_path ON IndexLibraries(CodebasePath);
            CREATE INDEX IF NOT EXISTS idx_libraries_active ON IndexLibraries(IsActive, UpdatedAt);

            -- JSON查询索引
            CREATE INDEX IF NOT EXISTS idx_watch_enabled ON IndexLibraries(JSON_EXTRACT(WatchConfig, '$.isEnabled'));
            CREATE INDEX IF NOT EXISTS idx_project_type ON IndexLibraries(JSON_EXTRACT(Metadata, '$.projectType'));

            -- 文件详情查询索引
            CREATE INDEX IF NOT EXISTS idx_files_library ON FileIndexDetails(LibraryId, LastIndexedAt);
            CREATE INDEX IF NOT EXISTS idx_files_path ON FileIndexDetails(LibraryId, RelativeFilePath);

            -- 任务查询索引
            CREATE INDEX IF NOT EXISTS idx_tasks_status ON BackgroundTasks(Status, CreatedAt);
            CREATE INDEX IF NOT EXISTS idx_tasks_library ON BackgroundTasks(LibraryId, Type);

            -- 事件查询索引
            CREATE INDEX IF NOT EXISTS idx_events_pending ON FileChangeEvents(Status, CreatedAt);
            CREATE INDEX IF NOT EXISTS idx_events_library ON FileChangeEvents(LibraryId, CreatedAt);
        ";

        await _connection.ExecuteAsync(createIndexesSql);
    }

    private async Task MigrateTablesAsync()
    {
        await AddColumnIfNotExistsAsync("BackgroundTasks", "Priority", "INTEGER DEFAULT 2");
        await AddColumnIfNotExistsAsync("BackgroundTasks", "FilePath", "VARCHAR(1000)");
        await AddColumnIfNotExistsAsync("FileChangeEvents", "LastRetryAt", "DATETIME");
    }

    private async Task AddColumnIfNotExistsAsync(string tableName, string columnName, string columnDefinition)
    {
        var columns = await _connection.QueryAsync($"PRAGMA table_info({tableName})");
        if (columns.All(c => ((string)c.name).ToLower() != columnName.ToLower()))
        {
            _logger.LogInformation("为表 {TableName} 添加新列 {ColumnName}...", tableName, columnName);
            await _connection.ExecuteAsync($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}");
            _logger.LogInformation("列 {ColumnName} 添加成功", columnName);
        }
    }

    public async Task<DbTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is SqliteConnection sqliteConnection)
        {
            _transaction = await sqliteConnection.BeginTransactionAsync(cancellationToken);
            return _transaction;
        }
        
        _transaction = (DbTransaction)_connection.BeginTransaction();
        return _transaction;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.CommitAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction != null)
        {
            await _transaction.RollbackAsync(cancellationToken);
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        _connection?.Dispose();
    }
}