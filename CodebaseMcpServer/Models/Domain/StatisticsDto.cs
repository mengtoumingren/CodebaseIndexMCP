namespace CodebaseMcpServer.Models.Domain;

/// <summary>
/// 统计信息DTO - JSON格式存储
/// </summary>
public class StatisticsDto
{
    public int IndexedSnippets { get; set; }
    public int TotalFiles { get; set; }
    public double LastIndexingDuration { get; set; }
    public long AverageFileSize { get; set; }
    public Dictionary<string, int> LanguageDistribution { get; set; } = new();
    public List<IndexingHistoryDto> IndexingHistory { get; set; } = new();
}

/// <summary>
/// 索引历史记录DTO
/// </summary>
public class IndexingHistoryDto
{
    public DateTime Date { get; set; }
    public double Duration { get; set; }
    public int FilesProcessed { get; set; }
    public int SnippetsCreated { get; set; }
}