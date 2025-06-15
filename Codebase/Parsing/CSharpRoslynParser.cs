using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodeSearch; // 引用 CodeSnippet 类

namespace Codebase.Parsing;

/// <summary>
/// 基于 Roslyn 的 C# 代码解析器 - Codebase 版本
/// </summary>
public class CSharpRoslynParser
{
    public bool SupportsFile(string filePath) 
        => Path.GetExtension(filePath).Equals(".cs", StringComparison.OrdinalIgnoreCase);
    
    public List<CodeSnippet> ParseCodeFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"[ERROR] 文件不存在: {filePath}");
            return new List<CodeSnippet>();
        }
            
        try
        {
            var content = File.ReadAllText(filePath);
            return ParseCodeContent(filePath, content);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 读取文件失败: {filePath}, 错误: {ex.Message}");
            return new List<CodeSnippet>();
        }
    }
    
    public List<CodeSnippet> ParseCodeContent(string filePath, string content)
    {
        try
        {
            Console.WriteLine($"[DEBUG] 开始使用 Roslyn 解析文件: {filePath}");
            
            // 检查内容是否为空
            if (string.IsNullOrWhiteSpace(content))
            {
                Console.WriteLine($"[WARNING] 文件内容为空: {filePath}");
                return new List<CodeSnippet>();
            }
            
            // 创建语法树
            var tree = CSharpSyntaxTree.ParseText(content, path: filePath);
            var root = tree.GetCompilationUnitRoot();
            
            // 检查语法错误
            var diagnostics = root.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (diagnostics.Any())
            {
                Console.WriteLine($"[WARNING] 文件 {filePath} 存在语法错误:");
                foreach (var diagnostic in diagnostics.Take(3)) // 只显示前3个错误
                {
                    var lineSpan = diagnostic.Location.GetLineSpan();
                    Console.WriteLine($"  行 {lineSpan.StartLinePosition.Line + 1}: {diagnostic.GetMessage()}");
                }
                // 即使有语法错误，也尝试继续解析
            }
            
            // 使用访问者模式遍历语法树
            var visitor = new CodeSnippetVisitor(filePath);
            visitor.Visit(root);
            
            var snippets = visitor.Snippets;
            Console.WriteLine($"[DEBUG] 文件 {filePath} 解析完成，提取 {snippets.Count} 个代码片段");
            
            return snippets;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 使用 Roslyn 解析文件失败: {filePath}, 错误: {ex.Message}");
            Console.WriteLine($"[ERROR] 堆栈跟踪: {ex.StackTrace}");
            return new List<CodeSnippet>();
        }
    }
}

/// <summary>
/// Roslyn 语法树访问者，用于提取代码片段 - Codebase 版本
/// </summary>
public class CodeSnippetVisitor : CSharpSyntaxWalker
{
    private readonly string _filePath;
    private readonly List<CodeSnippet> _snippets = new();
    private string? _currentNamespace;
    private string? _currentClass;
    
    public List<CodeSnippet> Snippets => _snippets;
    
    public CodeSnippetVisitor(string filePath)
    {
        _filePath = filePath;
    }
    
    public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        var previousNamespace = _currentNamespace;
        _currentNamespace = node.Name.ToString();
        
        base.VisitNamespaceDeclaration(node);
        
        _currentNamespace = previousNamespace;
    }
    
    public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
    {
        _currentNamespace = node.Name.ToString();
        base.VisitFileScopedNamespaceDeclaration(node);
    }
    
    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var previousClass = _currentClass;
        _currentClass = node.Identifier.ValueText;
        
        base.VisitClassDeclaration(node);
        
        _currentClass = previousClass;
    }
    
    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        var previousClass = _currentClass;
        _currentClass = node.Identifier.ValueText;
        
        base.VisitInterfaceDeclaration(node);
        
        _currentClass = previousClass;
    }
    
    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        var previousClass = _currentClass;
        _currentClass = node.Identifier.ValueText;
        
        base.VisitStructDeclaration(node);
        
        _currentClass = previousClass;
    }
    
    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        var previousClass = _currentClass;
        _currentClass = node.Identifier.ValueText;
        
        base.VisitRecordDeclaration(node);
        
        _currentClass = previousClass;
    }
    
    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var snippet = CreateSnippet(node, node.Identifier.ValueText, "方法");
        _snippets.Add(snippet);
        
        base.VisitMethodDeclaration(node);
    }
    
    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        var snippet = CreateSnippet(node, node.Identifier.ValueText, "构造函数");
        _snippets.Add(snippet);
        
        base.VisitConstructorDeclaration(node);
    }
    
    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        var snippet = CreateSnippet(node, node.Identifier.ValueText, "属性");
        _snippets.Add(snippet);
        
        base.VisitPropertyDeclaration(node);
    }
    
    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        foreach (var variable in node.Declaration.Variables)
        {
            var snippet = CreateSnippet(node, variable.Identifier.ValueText, "字段");
            _snippets.Add(snippet);
        }
        
        base.VisitFieldDeclaration(node);
    }
    
    public override void VisitEventDeclaration(EventDeclarationSyntax node)
    {
        var snippet = CreateSnippet(node, node.Identifier.ValueText, "事件");
        _snippets.Add(snippet);
        
        base.VisitEventDeclaration(node);
    }
    
    private CodeSnippet CreateSnippet(SyntaxNode node, string memberName, string memberType)
    {
        var lineSpan = node.GetLocation().GetLineSpan();
        
        return new CodeSnippet
        {
            FilePath = _filePath,
            Namespace = _currentNamespace,
            ClassName = _currentClass,
            MethodName = $"{memberName} ({memberType})",
            Code = node.ToString(),
            StartLine = lineSpan.StartLinePosition.Line + 1, // Roslyn uses 0-based
            EndLine = lineSpan.EndLinePosition.Line + 1
        };
    }
}

/// <summary>
/// 代码解析器工厂 - Codebase 版本
/// </summary>
public static class CodeParserFactory
{
    private static readonly CSharpRoslynParser _csharpParser = new();
    
    /// <summary>
    /// 获取指定文件的解析器
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>解析器实例，如果不支持则返回null</returns>
    public static CSharpRoslynParser? GetParser(string filePath)
    {
        // 当前只支持 C#，未来可扩展
        if (_csharpParser.SupportsFile(filePath))
            return _csharpParser;
            
        return null;
    }
    
    /// <summary>
    /// 检查是否支持指定文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>是否支持</returns>
    public static bool IsSupported(string filePath)
    {
        return _csharpParser.SupportsFile(filePath);
    }
    
    /// <summary>
    /// 获取 C# 解析器实例
    /// </summary>
    /// <returns>C# 解析器</returns>
    public static CSharpRoslynParser GetCSharpParser()
    {
        return _csharpParser;
    }
}