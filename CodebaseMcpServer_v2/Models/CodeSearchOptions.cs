namespace CodebaseMcpServer.Models;

/// <summary>
/// 代码搜索配置选项
/// </summary>
public class CodeSearchOptions
{
    public string DashScopeApiKey { get; set; } = string.Empty;
    public QdrantConfig QdrantConfig { get; set; } = new();
    public string DefaultCodebasePath { get; set; } = string.Empty;
    public SearchConfig SearchConfig { get; set; } = new();
}

/// <summary>
/// Qdrant 数据库配置
/// </summary>
public class QdrantConfig
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
    public string CollectionName { get; set; } = "codebase_embeddings";
}

/// <summary>
/// 搜索配置
/// </summary>
public class SearchConfig
{
    public int DefaultLimit { get; set; } = 10;
    public int MaxTokenLength { get; set; } = 8192;
    public int BatchSize { get; set; } = 10;
}