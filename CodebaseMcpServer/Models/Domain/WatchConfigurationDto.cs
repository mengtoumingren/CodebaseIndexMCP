namespace CodebaseMcpServer.Models.Domain;

/// <summary>
/// 文件监控配置DTO - JSON格式存储
/// </summary>
public class WatchConfigurationDto
{
    public List<string> FilePatterns { get; set; } = new();
    public List<string> ExcludePatterns { get; set; } = new();
    public bool IncludeSubdirectories { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public long MaxFileSize { get; set; } = 10 * 1024 * 1024; // 10MB
    public List<CustomFilterDto> CustomFilters { get; set; } = new();
}

/// <summary>
/// 自定义过滤器DTO
/// </summary>
public class CustomFilterDto
{
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}