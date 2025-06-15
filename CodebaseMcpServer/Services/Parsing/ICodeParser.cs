using CodebaseMcpServer.Models;

namespace CodebaseMcpServer.Services.Parsing;

/// <summary>
/// 代码解析器基础接口
/// </summary>
public interface ICodeParser
{
    /// <summary>
    /// 解析器支持的语言标识
    /// </summary>
    string Language { get; }
    
    /// <summary>
    /// 解析器显示名称
    /// </summary>
    string DisplayName { get; }
    
    /// <summary>
    /// 支持的文件扩展名
    /// </summary>
    IEnumerable<string> SupportedExtensions { get; }
    
    /// <summary>
    /// 检查是否支持指定文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否支持</returns>
    bool SupportsFile(string filePath);
    
    /// <summary>
    /// 解析代码文件并提取代码片段
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>代码片段列表</returns>
    List<CodeSnippet> ParseCodeFile(string filePath);
    
    /// <summary>
    /// 解析代码内容并提取代码片段
    /// </summary>
    /// <param name="filePath">文件路径（用于上下文）</param>
    /// <param name="content">文件内容</param>
    /// <returns>代码片段列表</returns>
    List<CodeSnippet> ParseCodeContent(string filePath, string content);
}