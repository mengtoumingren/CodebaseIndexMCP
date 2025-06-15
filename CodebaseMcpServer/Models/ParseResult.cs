namespace CodebaseMcpServer.Models;

/// <summary>
/// 代码解析结果模型
/// </summary>
public class ParseResult
{
    /// <summary>
    /// 解析是否成功
    /// </summary>
    public bool Success { get; set; } = true;
    
    /// <summary>
    /// 解析的语言类型
    /// </summary>
    public string Language { get; set; } = string.Empty;
    
    /// <summary>
    /// 解析的文件路径
    /// </summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// 提取的代码片段列表
    /// </summary>
    public List<CodeSnippet> Snippets { get; set; } = new();
    
    /// <summary>
    /// 错误信息（如果解析失败）
    /// </summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>
    /// 异常详情（如果解析失败）
    /// </summary>
    public Exception? Exception { get; set; }
    
    /// <summary>
    /// 解析完成时间
    /// </summary>
    public DateTime ParsedAt { get; set; } = DateTime.UtcNow;
}