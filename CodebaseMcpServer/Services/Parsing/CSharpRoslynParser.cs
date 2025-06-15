using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using CodebaseMcpServer.Models;

namespace CodebaseMcpServer.Services.Parsing;

/// <summary>
/// 简化的C# Roslyn解析器 - 满足索引构建需求
/// </summary>
public class CSharpRoslynParser : ICodeParser
{
    public string Language => "csharp";
    public string DisplayName => "C# (Roslyn)";
    public IEnumerable<string> SupportedExtensions => new[] { ".cs" };
    
    public bool SupportsFile(string filePath)
        => Path.GetExtension(filePath).Equals(".cs", StringComparison.OrdinalIgnoreCase);
    
    public List<CodeSnippet> ParseCodeFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[WARNING] 文件不存在: {filePath}");
            return new List<CodeSnippet>();
        }
        
        try
        {
            var content = File.ReadAllText(filePath);
            return ParseCodeContent(filePath, content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 解析文件失败: {filePath}, 错误: {ex.Message}");
            return new List<CodeSnippet>();
        }
    }
    
    public List<CodeSnippet> ParseCodeContent(string filePath, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return new List<CodeSnippet>();
        
        try
        {
            Console.WriteLine($"[DEBUG] 开始解析文件内容: {filePath}, 长度: {content.Length}");
            
            // 检查内容长度，避免解析过大文件导致栈溢出
            if (content.Length > 1_000_000) // 1MB 限制
            {
                Console.WriteLine($"[WARNING] 文件 {filePath} 过大 ({content.Length} 字符)，跳过解析以避免栈溢出");
                return new List<CodeSnippet>();
            }
            
            // 创建语法树
            Console.WriteLine($"[DEBUG] 创建语法树: {filePath}");
            var tree = CSharpSyntaxTree.ParseText(content, path: filePath);
            var root = tree.GetCompilationUnitRoot();
            
            Console.WriteLine($"[DEBUG] 语法树创建成功，检查诊断信息: {filePath}");
            
            // 检查是否有严重语法错误
            var errors = root.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();
            
            if (errors.Count > 10) // 如果错误过多，跳过解析
            {
                Console.WriteLine($"[WARNING] 文件 {filePath} 语法错误过多 ({errors.Count} 个错误)，跳过解析");
                return new List<CodeSnippet>();
            }
            
            // 检查语法树深度，避免过深的嵌套导致栈溢出
            var treeDepth = CalculateSyntaxTreeDepth(root);
            Console.WriteLine($"[DEBUG] 语法树深度: {treeDepth}, 文件: {filePath}");
            
            if (treeDepth > 100) // 限制语法树深度
            {
                Console.WriteLine($"[WARNING] 文件 {filePath} 语法树过深 (深度: {treeDepth})，可能导致栈溢出，跳过解析");
                return new List<CodeSnippet>();
            }
            
            // 使用访问者模式提取代码片段
            Console.WriteLine($"[DEBUG] 开始使用 SimpleCodeSnippetVisitor 访问语法树: {filePath}");
            var visitor = new SimpleCodeSnippetVisitor(filePath);
            
            // 设置递归深度限制
            try
            {
                visitor.Visit(root);
                Console.WriteLine($"[DEBUG] 语法树访问完成，提取到 {visitor.Snippets.Count} 个代码片段: {filePath}");
            }
            catch (StackOverflowException)
            {
                Console.WriteLine($"[ERROR] 栈溢出异常，文件 {filePath} 的语法结构过于复杂");
                return new List<CodeSnippet>();
            }
            catch (Exception visitorEx)
            {
                Console.WriteLine($"[ERROR] 访问者模式执行失败: {filePath}, 错误: {visitorEx.Message}");
                Console.WriteLine($"[ERROR] 异常类型: {visitorEx.GetType().Name}");
                return new List<CodeSnippet>();
            }
            
            return visitor.Snippets;
        }
        catch (StackOverflowException)
        {
            Console.WriteLine($"[ERROR] 栈溢出异常在解析 {filePath}");
            return new List<CodeSnippet>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Roslyn解析失败: {filePath}, 错误: {ex.Message}");
            Console.WriteLine($"[ERROR] 异常类型: {ex.GetType().Name}");
            if (ex.StackTrace != null)
            {
                Console.WriteLine($"[ERROR] 堆栈跟踪: {ex.StackTrace[..Math.Min(500, ex.StackTrace.Length)]}...");
            }
            return new List<CodeSnippet>();
        }
    }
    
    /// <summary>
    /// 计算语法树的最大深度
    /// </summary>
    private static int CalculateSyntaxTreeDepth(SyntaxNode node, int currentDepth = 0)
    {
        if (currentDepth > 200) // 防止计算深度时也栈溢出
        {
            return currentDepth;
        }
        
        var maxChildDepth = currentDepth;
        
        try
        {
            foreach (var child in node.ChildNodes())
            {
                var childDepth = CalculateSyntaxTreeDepth(child, currentDepth + 1);
                maxChildDepth = Math.Max(maxChildDepth, childDepth);
            }
        }
        catch
        {
            // 如果计算深度时出错，返回当前深度
            return currentDepth;
        }
        
        return maxChildDepth;
    }
}