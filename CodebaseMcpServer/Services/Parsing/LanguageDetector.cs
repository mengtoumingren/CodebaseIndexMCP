using CodebaseMcpServer.Models;

namespace CodebaseMcpServer.Services.Parsing;

/// <summary>
/// 基于文件扩展名的语言检测器
/// </summary>
public class LanguageDetector : ILanguageDetector
{
    /// <summary>
    /// 文件扩展名到语言信息的映射
    /// </summary>
    private static readonly Dictionary<string, LanguageInfo> ExtensionMap = new()
    {
        { ".cs", LanguageInfo.CSharp },
        { ".py", LanguageInfo.Python },
        { ".js", LanguageInfo.JavaScript },
        { ".ts", LanguageInfo.TypeScript },
        { ".cshtml", LanguageInfo.Cshtml }
    };
    
    /// <summary>
    /// 根据文件路径检测语言
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>语言标识符，如果不支持则返回"unknown"</returns>
    public string DetectLanguage(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "unknown";
        
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return ExtensionMap.TryGetValue(extension, out var info) 
            ? info.Id 
            : "unknown";
    }
    
    /// <summary>
    /// 获取指定语言的详细信息
    /// </summary>
    /// <param name="language">语言标识符</param>
    /// <returns>语言信息，如果不支持则返回null</returns>
    public LanguageInfo? GetLanguageInfo(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return null;
        
        return ExtensionMap.Values.FirstOrDefault(l => 
            l.Id.Equals(language, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// 获取所有支持的语言列表
    /// </summary>
    /// <returns>支持的语言信息列表</returns>
    public IEnumerable<LanguageInfo> GetSupportedLanguages()
    {
        return ExtensionMap.Values.Distinct();
    }
}