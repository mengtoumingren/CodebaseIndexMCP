using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using CodebaseMcpServer.Models;

namespace CodebaseMcpServer.Services.Parsing;

/// <summary>
/// Roslyn 语法树访问者，用于提取代码片段
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
    
    public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
    {
        var snippet = CreateSnippet(node, $"~{node.Identifier.ValueText}", "析构函数");
        _snippets.Add(snippet);
        
        base.VisitDestructorDeclaration(node);
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
    
    public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
    {
        foreach (var variable in node.Declaration.Variables)
        {
            var snippet = CreateSnippet(node, variable.Identifier.ValueText, "事件字段");
            _snippets.Add(snippet);
        }
        
        base.VisitEventFieldDeclaration(node);
    }
    
    public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        var snippet = CreateSnippet(node, $"operator {node.OperatorToken.ValueText}", "运算符");
        _snippets.Add(snippet);
        
        base.VisitOperatorDeclaration(node);
    }
    
    public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        var keyword = node.ImplicitOrExplicitKeyword.ValueText;
        var type = node.Type.ToString();
        var snippet = CreateSnippet(node, $"{keyword} operator {type}", "转换运算符");
        _snippets.Add(snippet);
        
        base.VisitConversionOperatorDeclaration(node);
    }
    
    public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
    {
        var snippet = CreateSnippet(node, "this[]", "索引器");
        _snippets.Add(snippet);
        
        base.VisitIndexerDeclaration(node);
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