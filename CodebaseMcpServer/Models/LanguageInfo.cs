namespace CodebaseMcpServer.Models;

/// <summary>
/// 语言信息模型
/// </summary>
public record LanguageInfo(
    string Id,           // 语言标识，如 "csharp"
    string DisplayName,  // 显示名称，如 "C#"
    string[] Extensions, // 文件扩展名，如 [".cs"]
    string MimeType = "text/plain")  // MIME类型，如 "text/x-csharp"
{
    public static LanguageInfo CSharp => new(
        "csharp", 
        "C#", 
        new[] { ".cs" }, 
        "text/x-csharp");
    
    public static LanguageInfo Python => new(
        "python", 
        "Python", 
        new[] { ".py" }, 
        "text/x-python");
    
    public static LanguageInfo JavaScript => new(
        "javascript", 
        "JavaScript", 
        new[] { ".js" }, 
        "text/javascript");
    
    public static LanguageInfo TypeScript => new(
        "typescript", 
        "TypeScript", 
        new[] { ".ts" }, 
        "text/typescript");
}