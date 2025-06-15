# 多语言代码解析框架使用指南

## 📝 概述

该框架提供了可扩展的多语言代码解析能力，当前专门优化了C#解析器以满足索引构建需求，同时为将来扩展其他语言预留了标准化接口。

## 🚀 快速开始

### 使用C#解析器

```csharp
using CodebaseMcpServer.Services.Parsing;

// 检查文件是否支持
if (CodeParserFactory.IsSupported("MyClass.cs"))
{
    // 获取解析器
    var parser = CodeParserFactory.GetParser("MyClass.cs");
    if (parser != null)
    {
        // 解析文件
        var snippets = parser.ParseCodeFile("MyClass.cs");
        
        Console.WriteLine($"解析语言: {parser.Language}");
        Console.WriteLine($"提取了 {snippets.Count} 个代码片段");
    }
}
```

### 在现有代码中使用

```csharp
// 在 EnhancedCodeSemanticSearch 中
var snippets = ExtractCodeSnippets(filePath); // 新方法，支持多语言
// 或
var snippets = ExtractCSharpSnippets(filePath); // 向后兼容方法
```

## 🏗️ 架构说明

### 核心组件

1. **ICodeParser** - 解析器基础接口
   - `Language` - 语言标识符
   - `DisplayName` - 显示名称  
   - `SupportedExtensions` - 支持的文件扩展名
   - `ParseCodeFile()` - 解析文件方法
   - `ParseCodeContent()` - 解析内容方法

2. **LanguageDetector** - 语言检测器
   - 基于文件扩展名自动识别语言类型
   - 支持 .cs、.py、.js、.ts 等扩展名

3. **CodeParserFactory** - 解析器工厂
   - 统一的解析器获取入口
   - 支持动态注册新解析器
   - 提供语言支持检查

### 当前支持的语言

| 语言 | 扩展名 | 状态 | 解析器 |
|------|--------|------|--------|
| C# | .cs | ✅ 完全支持 | CSharpRoslynParser |
| Python | .py | 🔄 预留接口 | - |
| JavaScript | .js | 🔄 预留接口 | - |
| TypeScript | .ts | 🔄 预留接口 | - |

## 🔧 扩展其他语言

### 实现新的解析器

```csharp
public class PythonParser : ICodeParser
{
    public string Language => "python";
    public string DisplayName => "Python";
    public IEnumerable<string> SupportedExtensions => new[] { ".py" };
    
    public bool SupportsFile(string filePath) 
        => Path.GetExtension(filePath).Equals(".py", StringComparison.OrdinalIgnoreCase);
    
    public List<CodeSnippet> ParseCodeFile(string filePath)
    {
        // 实现Python文件解析逻辑
        return new List<CodeSnippet>();
    }
    
    public List<CodeSnippet> ParseCodeContent(string filePath, string content)
    {
        // 实现Python内容解析逻辑
        return new List<CodeSnippet>();
    }
}
```

### 注册新解析器

```csharp
// 在应用启动时注册
CodeParserFactory.RegisterParser("python", () => new PythonParser());
```

## 📊 C#解析器特性

### 支持的代码结构

- ✅ 类 (Class)
- ✅ 接口 (Interface)  
- ✅ 结构体 (Struct)
- ✅ 记录 (Record) - C# 9+
- ✅ 枚举 (Enum)
- ✅ 方法 (Method)
- ✅ 构造函数 (Constructor)
- ✅ 属性 (Property)
- ✅ 字段 (Field)
- ✅ 事件 (Event)

### 现代C#特性支持

- ✅ 文件作用域命名空间 (C# 10)
- ✅ 记录类型 (C# 9)
- ✅ 嵌套类型
- ✅ 泛型支持
- ✅ 特性标注

### 智能特性

- **错误容错**: 语法错误过多时跳过解析
- **代码截取**: 超长代码片段智能截取
- **性能优化**: 简化实现，专注索引需求

## 🛠️ 配置和定制

### 修改语言检测

```csharp
// 扩展 LanguageDetector 以支持更多文件类型
public class CustomLanguageDetector : LanguageDetector
{
    private static readonly Dictionary<string, LanguageInfo> CustomExtensions = new()
    {
        { ".jsx", new LanguageInfo("javascript", "JSX", new[] { ".jsx" }) },
        { ".vue", new LanguageInfo("vue", "Vue", new[] { ".vue" }) }
    };
    
    // 重写检测逻辑...
}
```

### 自定义代码片段结构

当前`CodeSnippet`模型：
```csharp
public class CodeSnippet
{
    public string FilePath { get; set; }
    public string? Namespace { get; set; }
    public string? ClassName { get; set; }
    public string? MethodName { get; set; }
    public string Code { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
}
```

## 🔍 故障排除

### 常见问题

1. **解析器返回null**
   - 检查文件扩展名是否受支持
   - 确认已注册对应语言的解析器

2. **代码片段为空**
   - 检查文件内容是否有效
   - 确认语法错误数量不超过限制

3. **性能问题**
   - 大文件会自动截取以提高性能
   - 语法错误过多的文件会被跳过

### 调试信息

解析器会输出详细的调试信息：
```
[DEBUG] 开始解析文件: MyClass.cs
[DEBUG] 文件 MyClass.cs 解析完成，语言: csharp，提取 15 个代码片段
```

## 📈 性能指标

| 指标 | C#解析器 | 说明 |
|------|----------|------|
| 解析准确率 | ~99% | 基于Roslyn语法树 |
| 错误容忍度 | 10个错误 | 超过则跳过文件 |
| 代码长度限制 | 2000字符 | 超过则智能截取 |
| 支持文件大小 | 无限制 | 内存允许范围内 |

## 🔗 相关文档

- [Multi-Language-Parser-Framework-Design.md](Multi-Language-Parser-Framework-Design.md) - 详细设计文档
- [CSharp-Roslyn-Parser-Upgrade-Plan.md](CSharp-Roslyn-Parser-Upgrade-Plan.md) - C#解析器升级计划
- 项目源码：`CodebaseMcpServer/Services/Parsing/` 目录

---

**最后更新**: 2025-06-15  
**框架版本**: v1.0  
**状态**: 生产就绪