using CodebaseMcpServer.Models;

namespace CodebaseMcpServer.Services.Parsing;

/// <summary>
/// 代码解析器工厂
/// </summary>
public static class CodeParserFactory
{
    private static readonly ILanguageDetector LanguageDetector = new LanguageDetector();
    private static readonly Dictionary<string, Func<ICodeParser>> ParserFactories = new()
    {
        { "csharp", () => new CSharpRoslynParser() }
        // 将来可以添加其他语言解析器
        // { "python", () => new PythonParser() },
        // { "javascript", () => new JavaScriptParser() }
    };
    
    /// <summary>
    /// 获取指定文件的解析器
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>解析器实例，如果不支持则返回null</returns>
    public static ICodeParser? GetParser(string filePath)
    {
        var language = LanguageDetector.DetectLanguage(filePath);
        return GetParserByLanguage(language);
    }
    
    /// <summary>
    /// 根据语言获取解析器
    /// </summary>
    /// <param name="language">语言标识符</param>
    /// <returns>解析器实例，如果不支持则返回null</returns>
    public static ICodeParser? GetParserByLanguage(string language)
    {
        return ParserFactories.TryGetValue(language, out var factory)
            ? factory()
            : null;
    }
    
    /// <summary>
    /// 检查是否支持指定文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否支持</returns>
    public static bool IsSupported(string filePath)
    {
        var language = LanguageDetector.DetectLanguage(filePath);
        return ParserFactories.ContainsKey(language);
    }
    
    /// <summary>
    /// 获取所有支持的语言
    /// </summary>
    /// <returns>支持的语言信息列表</returns>
    public static IEnumerable<LanguageInfo> GetSupportedLanguages()
    {
        return LanguageDetector.GetSupportedLanguages()
            .Where(lang => ParserFactories.ContainsKey(lang.Id));
    }
    
    /// <summary>
    /// 注册新的解析器（用于将来扩展）
    /// </summary>
    /// <param name="language">语言标识符</param>
    /// <param name="factory">解析器工厂方法</param>
    public static void RegisterParser(string language, Func<ICodeParser> factory)
    {
        ParserFactories[language] = factory;
    }
    
    /// <summary>
    /// 获取 C# 解析器实例（向后兼容）
    /// </summary>
    /// <returns>C# 解析器</returns>
    public static ICodeParser? GetCSharpParser()
    {
        return GetParserByLanguage("csharp");
    }
}