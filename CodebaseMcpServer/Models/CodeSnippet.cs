namespace CodebaseMcpServer.Models;

/// <summary>
/// 表示一个代码片段
/// </summary>
public class CodeSnippet
{
    public string FilePath { get; set; } = string.Empty;
    public string? Namespace { get; set; }
    public string? ClassName { get; set; }
    public string? MethodName { get; set; }
    public string Code { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}