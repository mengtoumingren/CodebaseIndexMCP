using CodebaseMcpServer.Models;

namespace CodebaseMcpServer.Services.Parsing;

/// <summary>
/// 语言检测器接口
/// </summary>
public interface ILanguageDetector
{
    /// <summary>
    /// 根据文件路径检测语言
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>语言标识符</returns>
    string DetectLanguage(string filePath);
    
    /// <summary>
    /// 获取指定语言的详细信息
    /// </summary>
    /// <param name="language">语言标识符</param>
    /// <returns>语言信息，如果不支持则返回null</returns>
    LanguageInfo? GetLanguageInfo(string language);
    
    /// <summary>
    /// 获取所有支持的语言列表
    /// </summary>
    /// <returns>支持的语言信息列表</returns>
    IEnumerable<LanguageInfo> GetSupportedLanguages();
}