using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodebaseMcpServer.Models.Domain;

/// <summary>
/// 索引库实体 - SQLite + JSON 混合模式
/// </summary>
public class IndexLibrary
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CodebasePath { get; set; } = string.Empty;
    public string CollectionName { get; set; } = string.Empty;
    public IndexLibraryStatus Status { get; set; } = IndexLibraryStatus.Pending;
    
    // JSON列 - 存储为字符串，运行时序列化/反序列化
    public string WatchConfig { get; set; } = "{}";
    public string Statistics { get; set; } = "{}";
    public string Metadata { get; set; } = "{}";
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastIndexedAt { get; set; }
    public int TotalFiles { get; set; }
    public int IndexedSnippets { get; set; }
    public bool IsActive { get; set; } = true;
    
    // 运行时属性 - 不映射到数据库
    [JsonIgnore]
    public WatchConfigurationDto WatchConfigObject 
    { 
        get => JsonSerializer.Deserialize<WatchConfigurationDto>(WatchConfig) ?? new();
        set => WatchConfig = JsonSerializer.Serialize(value);
    }
    
    [JsonIgnore]
    public StatisticsDto StatisticsObject
    {
        get => JsonSerializer.Deserialize<StatisticsDto>(Statistics) ?? new();
        set => Statistics = JsonSerializer.Serialize(value);
    }
    
    [JsonIgnore]
    public MetadataDto MetadataObject
    {
        get => JsonSerializer.Deserialize<MetadataDto>(Metadata) ?? new();
        set => Metadata = JsonSerializer.Serialize(value);
    }
}

/// <summary>
/// 索引库状态枚举
/// </summary>
public enum IndexLibraryStatus
{
    Pending,
    Indexing,
    Completed,
    Failed,
    Cancelled
}