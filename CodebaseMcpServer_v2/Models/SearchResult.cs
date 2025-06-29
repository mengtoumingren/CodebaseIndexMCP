namespace CodebaseMcpServer.Models;

/// <summary>
/// 表示搜索结果
/// </summary>
public class SearchResult
{
    public float Score { get; set; }
    public CodeSnippet Snippet { get; set; } = new();
}