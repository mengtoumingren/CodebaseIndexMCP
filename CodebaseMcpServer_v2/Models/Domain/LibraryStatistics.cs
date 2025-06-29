namespace CodebaseMcpServer.Models.Domain;

/// <summary>
/// 全局库统计信息
/// </summary>
public class LibraryStatistics
{
    public int TotalLibraries { get; set; }
    public int ActiveLibraries { get; set; }
    public int TotalSnippets { get; set; }
    public int TotalFiles { get; set; }
    public long TotalSizeBytes { get; set; }
    public Dictionary<string, int> ProjectTypeDistribution { get; set; } = new();
    public Dictionary<string, int> LanguageDistribution { get; set; } = new();
    public Dictionary<string, int> StatusDistribution { get; set; } = new();
    public DateTime LastCalculatedAt { get; set; }
    public double AverageIndexingTime { get; set; }
    public List<TopLibraryDto> TopLibrariesBySize { get; set; } = new();
    public List<RecentActivityDto> RecentActivity { get; set; } = new();
}

/// <summary>
/// 顶级库信息DTO
/// </summary>
public class TopLibraryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int IndexedSnippets { get; set; }
    public int TotalFiles { get; set; }
    public long SizeBytes { get; set; }
}

/// <summary>
/// 最近活动DTO
/// </summary>
public class RecentActivityDto
{
    public int LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;
    public string ActivityType { get; set; } = string.Empty;
    public DateTime ActivityTime { get; set; }
    public string Description { get; set; } = string.Empty;
}