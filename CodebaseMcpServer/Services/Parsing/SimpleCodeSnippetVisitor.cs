using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodebaseMcpServer.Models;

namespace CodebaseMcpServer.Services.Parsing;

/// <summary>
/// 简化的代码片段访问者 - 专注于核心成员提取
/// </summary>
public class SimpleCodeSnippetVisitor : CSharpSyntaxWalker
{
    private readonly string _filePath;
    private readonly List<CodeSnippet> _snippets = new();
    private string? _currentNamespace;
    private string? _currentClass;
    
    public List<CodeSnippet> Snippets => _snippets;
    
    public SimpleCodeSnippetVisitor(string filePath)
    {
        _filePath = filePath;
    }
    
    #region 命名空间处理
    
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
    
    #endregion
    
    #region 类型声明处理
    
    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        ProcessTypeDeclaration(node, node.Identifier.ValueText, "类");
    }
    
    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        ProcessTypeDeclaration(node, node.Identifier.ValueText, "接口");
    }
    
    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        ProcessTypeDeclaration(node, node.Identifier.ValueText, "结构体");
    }
    
    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        ProcessTypeDeclaration(node, node.Identifier.ValueText, "记录");
    }
    
    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        ProcessTypeDeclaration(node, node.Identifier.ValueText, "枚举");
    }
    
    private void ProcessTypeDeclaration(SyntaxNode node, string typeName, string typeKind)
    {
        var previousClass = _currentClass;
        _currentClass = typeName;
        
        try
        {
            Console.WriteLine($"[DEBUG] 处理类型声明: {typeKind} {typeName} (当前栈深度检查)");
            
            var snippet = CreateCodeSnippet(node, typeName, typeKind);
            _snippets.Add(snippet);
            
            // 限制递归深度，避免栈溢出
            // 对于类型声明，我们只处理直接成员，不深度遍历嵌套内容
            // base.Visit(node); // 移除可能导致深度递归的调用
            
            // 手动访问子节点，但避免深度递归
            foreach (var child in node.ChildNodes())
            {
                try
                {
                    // 只处理方法、属性、字段等成员声明，避免处理嵌套类型
                    if (child is MethodDeclarationSyntax methodDecl)
                    {
                        VisitMethodDeclaration(methodDecl);
                    }
                    else if (child is PropertyDeclarationSyntax propDecl)
                    {
                        VisitPropertyDeclaration(propDecl);
                    }
                    else if (child is FieldDeclarationSyntax fieldDecl)
                    {
                        VisitFieldDeclaration(fieldDecl);
                    }
                    else if (child is ConstructorDeclarationSyntax ctorDecl)
                    {
                        VisitConstructorDeclaration(ctorDecl);
                    }
                    else if (child is EventDeclarationSyntax eventDecl)
                    {
                        VisitEventDeclaration(eventDecl);
                    }
                    // 跳过嵌套类型声明，避免深度递归
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARNING] 处理子节点失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] 处理类型声明失败: {typeName}, 错误: {ex.Message}");
        }
        finally
        {
            _currentClass = previousClass;
        }
    }
    
    #endregion
    
    #region 成员声明处理
    
    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var methodName = node.Identifier.ValueText;
        var snippet = CreateCodeSnippet(node, methodName, "方法");
        _snippets.Add(snippet);
        
        // 不继续访问方法体内容，避免过深的嵌套
    }
    
    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        var constructorName = node.Identifier.ValueText;
        var snippet = CreateCodeSnippet(node, constructorName, "构造函数");
        _snippets.Add(snippet);
    }
    
    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        var propertyName = node.Identifier.ValueText;
        var snippet = CreateCodeSnippet(node, propertyName, "属性");
        _snippets.Add(snippet);
    }
    
    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        // 处理字段声明（可能包含多个变量）
        foreach (var variable in node.Declaration.Variables)
        {
            var fieldName = variable.Identifier.ValueText;
            var snippet = CreateCodeSnippet(node, fieldName, "字段");
            _snippets.Add(snippet);
        }
    }
    
    public override void VisitEventDeclaration(EventDeclarationSyntax node)
    {
        var eventName = node.Identifier.ValueText;
        var snippet = CreateCodeSnippet(node, eventName, "事件");
        _snippets.Add(snippet);
    }
    
    #endregion
    
    #region 代码片段创建
    
    private CodeSnippet CreateCodeSnippet(SyntaxNode node, string memberName, string memberType)
    {
        try
        {
            var location = node.GetLocation();
            var lineSpan = location.GetLineSpan();
            
            // 限制代码长度，避免过大的代码片段
            var code = node.ToString();
            if (code.Length > 2000)
            {
                code = code[..1950] + "\n// ... 代码过长已截取 ...";
            }
            
            return new CodeSnippet
            {
                FilePath = _filePath,
                Namespace = _currentNamespace,
                ClassName = _currentClass,
                MethodName = $"{memberName} ({memberType})",
                Code = code,
                StartLine = lineSpan.StartLinePosition.Line + 1,
                EndLine = lineSpan.EndLinePosition.Line + 1
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARNING] 创建代码片段失败: {memberName}, 错误: {ex.Message}");
            
            // 返回基础的代码片段
            return new CodeSnippet
            {
                FilePath = _filePath,
                Namespace = _currentNamespace,
                ClassName = _currentClass,
                MethodName = $"{memberName} ({memberType})",
                Code = $"// 无法提取代码: {ex.Message}",
                StartLine = 1,
                EndLine = 1
            };
        }
    }
    
    #endregion
}