namespace CodebaseMcpServer.Domain.ValueObjects;

/// <summary>
/// 元数据DTO - JSON格式存储
/// </summary>
public class MetadataDto
{
    public string ProjectType { get; set; } = "unknown";
    public string Framework { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string Priority { get; set; } = "normal";
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, object> CustomSettings { get; set; } = new();
}
