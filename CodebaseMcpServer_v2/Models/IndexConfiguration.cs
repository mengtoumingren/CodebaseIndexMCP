using System.Text.Json.Serialization;

namespace CodebaseMcpServer.Models;

/// <summary>
/// 索引配置根模型
/// </summary>
public class IndexConfiguration
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";
    
    [JsonPropertyName("lastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    
    [JsonPropertyName("codebaseMappings")]
    public List<CodebaseMapping> CodebaseMappings { get; set; } = new();
    
    [JsonPropertyName("globalSettings")]
    public GlobalSettings GlobalSettings { get; set; } = new();
}

/// <summary>
/// 代码库映射模型
/// </summary>
public class CodebaseMapping
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("codebasePath")]
    public string CodebasePath { get; set; } = string.Empty;
    
    [JsonPropertyName("normalizedPath")]
    public string NormalizedPath { get; set; } = string.Empty;
    
    [JsonPropertyName("collectionName")]
    public string CollectionName { get; set; } = string.Empty;
    
    [JsonPropertyName("friendlyName")]
    public string FriendlyName { get; set; } = string.Empty;
    
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [JsonPropertyName("lastIndexed")]
    public DateTime? LastIndexed { get; set; }
    
    [JsonPropertyName("indexingStatus")]
    public string IndexingStatus { get; set; } = "pending";
    
    [JsonPropertyName("isMonitoring")]
    public bool IsMonitoring { get; set; } = true;
    
    [JsonPropertyName("statistics")]
    public IndexStatistics Statistics { get; set; } = new();
    
    [JsonPropertyName("watcherConfig")]
    public WatcherConfig WatcherConfig { get; set; } = new();
    
    [JsonPropertyName("fileIndexDetails")]
    public List<FileIndexDetail> FileIndexDetails { get; set; } = new();
}

/// <summary>
/// 文件索引详情 - 用于增量重建索引
/// </summary>
public class FileIndexDetail
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty; // 文件相对路径 (相对于 codebasePath)

    [JsonPropertyName("normalizedFilePath")]
    public string NormalizedFilePath { get; set; } = string.Empty; // 规范化的文件相对路径

    [JsonPropertyName("lastIndexed")]
    public DateTime LastIndexed { get; set; } // 文件上次成功索引的时间 (UTC)

    [JsonPropertyName("fileHash")] // 可选，用于更精确地判断文件内容是否变化
    public string? FileHash { get; set; }
}

/// <summary>
/// 索引统计信息
/// </summary>
public class IndexStatistics
{
    [JsonPropertyName("totalFiles")]
    public int TotalFiles { get; set; }
    
    [JsonPropertyName("indexedSnippets")]
    public int IndexedSnippets { get; set; }
    
    [JsonPropertyName("lastIndexingDuration")]
    public string LastIndexingDuration { get; set; } = string.Empty;
    
    [JsonPropertyName("lastUpdateTime")]
    public DateTime? LastUpdateTime { get; set; }
}

/// <summary>
/// 文件监控配置
/// </summary>
public class WatcherConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
    
    [JsonPropertyName("includeSubdirectories")]
    public bool IncludeSubdirectories { get; set; } = true;
    
    [JsonPropertyName("fileExtensions")]
    public List<string> FileExtensions { get; set; } = new() { ".cs" };
    
    [JsonPropertyName("excludeDirectories")]
    public List<string> ExcludeDirectories { get; set; } = new() { "bin", "obj", ".git" };
}

/// <summary>
/// 索引处理设置
/// </summary>
public class IndexingSettings
{
    [JsonPropertyName("batchSize")]
    public int BatchSize { get; set; } = 10;
    
    [JsonPropertyName("enableRealTimeProgress")]
    public bool EnableRealTimeProgress { get; set; } = true;
    
    [JsonPropertyName("enableBatchLogging")]
    public bool EnableBatchLogging { get; set; } = true;
    
    [JsonPropertyName("maxConcurrentBatches")]
    public int MaxConcurrentBatches { get; set; } = 1;
}

/// <summary>
/// 全局设置
/// </summary>
public class GlobalSettings
{
    [JsonPropertyName("maxConcurrentIndexing")]
    public int MaxConcurrentIndexing { get; set; } = 3;
    
    [JsonPropertyName("indexingQueueSize")]
    public int IndexingQueueSize { get; set; } = 100;
    
    [JsonPropertyName("autoCleanupDays")]
    public int AutoCleanupDays { get; set; } = 30;
    
    [JsonPropertyName("indexingSettings")]
    public IndexingSettings IndexingSettings { get; set; } = new();
}