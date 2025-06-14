using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using CodebaseMcpServer.Models;

namespace CodebaseMcpServer.Services;

// Embeddings API响应模型
public class EmbeddingResponse
{
    [JsonProperty("data")]
    public List<EmbeddingData> Data { get; set; } = new();
    
    [JsonProperty("model")]
    public string Model { get; set; } = string.Empty;
    
    [JsonProperty("object")]
    public string Object { get; set; } = string.Empty;
    
    [JsonProperty("usage")]
    public UsageInfo Usage { get; set; } = new();
    
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;
}

public class EmbeddingData
{
    [JsonProperty("embedding")]
    public List<float> Embedding { get; set; } = new();
    
    [JsonProperty("index")]
    public int Index { get; set; }
    
    [JsonProperty("object")]
    public string Object { get; set; } = string.Empty;
}

public class UsageInfo
{
    [JsonProperty("prompt_tokens")]
    public int PromptTokens { get; set; }
    
    [JsonProperty("total_tokens")]
    public int TotalTokens { get; set; }
}

/// <summary>
/// 代码语义搜索服务实现
/// </summary>
public class CodeSemanticSearch : ICodeSearchService
{
    private readonly CodeSearchOptions _options;
    private readonly QdrantClient _client;
    private readonly int _embeddingDim = 1024;
    
    // API限制常量
    private const int MAX_BATCH_SIZE = 10; // text-embedding-v4模型最多支持10条
    private const int MAX_TOKEN_LENGTH = 8192; // text-embedding-v4每条最长支持8,192 Token
    private const int APPROX_CHARS_PER_TOKEN = 4; // 大约每4个字符=1个Token（估算值）

    public CodeSemanticSearch(IOptions<CodeSearchOptions> options)
    {
        _options = options.Value;
        
        // 连接Qdrant客户端
        Console.WriteLine($"[DEBUG] 尝试连接Qdrant服务器: {_options.QdrantConfig.Host}:{_options.QdrantConfig.Port}");
        _client = new QdrantClient(_options.QdrantConfig.Host, _options.QdrantConfig.Port);
        
        // 确保集合存在
        Console.WriteLine("[DEBUG] 开始初始化集合...");
        EnsureCollection().Wait();
        Console.WriteLine("[DEBUG] 集合初始化完成");
    }

    private async Task EnsureCollection()
    {
        try
        {
            Console.WriteLine($"[DEBUG] 检查集合是否存在: {_options.QdrantConfig.CollectionName}");
            await _client.GetCollectionInfoAsync(_options.QdrantConfig.CollectionName);
            Console.WriteLine($"[DEBUG] 集合 {_options.QdrantConfig.CollectionName} 已存在");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] 集合不存在或连接失败: {ex.Message}");
            Console.WriteLine($"[DEBUG] 异常类型: {ex.GetType().Name}");
            
            // 创建新集合
            Console.WriteLine($"[DEBUG] 尝试创建新集合: {_options.QdrantConfig.CollectionName}");
            await _client.CreateCollectionAsync(
                _options.QdrantConfig.CollectionName,
                new VectorParams
                {
                    Size = (ulong)_embeddingDim,
                    Distance = Distance.Cosine
                });
            Console.WriteLine($"[DEBUG] 集合 {_options.QdrantConfig.CollectionName} 创建成功");
        }
    }

    public async Task<List<SearchResult>> SearchAsync(string query, string? codebasePath = null, int limit = 10)
    {
        // 使用提供的路径或默认路径
        var targetPath = codebasePath ?? _options.DefaultCodebasePath;
        
        // 检查代码库是否已索引
        if (!await IsCodebaseIndexedAsync(targetPath))
        {
            Console.WriteLine($"[INFO] 代码库 {targetPath} 尚未索引，开始索引...");
            await ProcessCodebase(targetPath);
        }

        // 生成查询的嵌入向量
        var queryEmbedding = await GetEmbeddings(new List<string> { query });
        
        // 执行搜索
        var queryVectorData = queryEmbedding[0].ToArray();
        
        var searchResult = await _client.SearchAsync(
            _options.QdrantConfig.CollectionName,
            queryVectorData,
            limit: (ulong)limit);
        
        // 处理结果
        var results = new List<SearchResult>();
        foreach (var hit in searchResult)
        {
            var snippet = new CodeSnippet
            {
                FilePath = hit.Payload["filePath"].StringValue,
                Namespace = hit.Payload["namespace"].StringValue,
                ClassName = hit.Payload["className"].StringValue,
                MethodName = hit.Payload["methodName"].StringValue,
                Code = hit.Payload["code"].StringValue,
                StartLine = (int)hit.Payload["startLine"].IntegerValue,
                EndLine = (int)hit.Payload["endLine"].IntegerValue
            };
            
            results.Add(new SearchResult
            {
                Score = hit.Score,
                Snippet = snippet
            });
        }
        
        return results;
    }

    public async Task<bool> IsCodebaseIndexedAsync(string codebasePath)
    {
        try
        {
            // 简单检查：查询是否有来自该路径的数据
            var searchResult = await _client.SearchAsync(
                _options.QdrantConfig.CollectionName,
                new float[_embeddingDim], // 零向量
                limit: 1);
            
            return searchResult.Any();
        }
        catch
        {
            return false;
        }
    }
    
    private async Task<int> ProcessCodebase(string codebasePath, List<string>? filePatterns = null)
    {
        filePatterns ??= new List<string> { "*.cs" };
        var allSnippets = new List<CodeSnippet>();
        
        // 遍历代码库中的所有匹配文件
        foreach (var pattern in filePatterns)
        {
            foreach (var filePath in Directory.GetFiles(codebasePath, pattern, SearchOption.AllDirectories))
            {
                var snippets = ExtractCSharpSnippets(filePath);
                allSnippets.AddRange(snippets);
            }
        }
        
        // 批量处理代码片段
        await BatchIndexSnippets(allSnippets);
        
        return allSnippets.Count;
    }

    /// <summary>
    /// 估算文本的Token数量
    /// </summary>
    private int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
            
        var wordCount = text.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var chineseCharCount = text.Count(c => c >= 0x4e00 && c <= 0x9fff); // 中文字符范围
        
        // 估算：英文单词 + 中文字符 + 符号补偿
        var estimatedTokens = wordCount + chineseCharCount + (text.Length - wordCount - chineseCharCount) / 4;
        
        return Math.Max(estimatedTokens, text.Length / APPROX_CHARS_PER_TOKEN);
    }

    /// <summary>
    /// 智能截断文本，保持代码结构完整性
    /// </summary>
    private string TruncateText(string text, int maxTokens)
    {
        if (EstimateTokenCount(text) <= maxTokens)
            return text;
            
        // 按行分割，尝试保持代码结构
        var lines = text.Split('\n');
        var result = new StringBuilder();
        var currentTokens = 0;
        
        foreach (var line in lines)
        {
            var lineTokens = EstimateTokenCount(line + "\n");
            if (currentTokens + lineTokens > maxTokens)
            {
                break;
            }
            result.AppendLine(line);
            currentTokens += lineTokens;
        }
        
        var truncated = result.ToString().TrimEnd();
        Console.WriteLine($"[INFO] 文本从 {EstimateTokenCount(text)} Token 截断至 {EstimateTokenCount(truncated)} Token");
        
        return truncated;
    }

    private async Task<List<List<float>>> GetEmbeddings(List<string> texts)
    {
        // 检查批量大小限制
        if (texts.Count > MAX_BATCH_SIZE)
        {
            throw new ArgumentException($"批量大小不能超过 {MAX_BATCH_SIZE}，当前为 {texts.Count}");
        }
        
        // 检查每个文本的长度限制并智能截断
        var processedTexts = new List<string>();
        for (int i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            var estimatedTokens = EstimateTokenCount(text);
            
            if (estimatedTokens > MAX_TOKEN_LENGTH)
            {
                Console.WriteLine($"[WARNING] 文本 {i} 长度过长 (约{estimatedTokens}个Token)，将被智能截断至 {MAX_TOKEN_LENGTH} Token");
                text = TruncateText(text, MAX_TOKEN_LENGTH);
            }
            else
            {
                Console.WriteLine($"[DEBUG] 文本 {i} 长度: 约{estimatedTokens}个Token，符合限制");
            }
            processedTexts.Add(text);
        }
        
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.DashScopeApiKey}");
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
        var payload = new
        {
            model = "text-embedding-v4",
            input = processedTexts,
            dimension = "1024",
            encoding_format = "float"
        };
        
        var content = new StringContent(
            JsonConvert.SerializeObject(payload),
            Encoding.UTF8,
            new MediaTypeHeaderValue("application/json"));
            
        try
        {
            var response = await httpClient.PostAsync(
                "https://dashscope.aliyuncs.com/compatible-mode/v1/embeddings",
                content);
            
            response.EnsureSuccessStatusCode();
            var jsonResponse = await response.Content.ReadAsStringAsync();
            var embeddingResponse = JsonConvert.DeserializeObject<EmbeddingResponse>(jsonResponse);
            
            return embeddingResponse?.Data
                ?.Select(item => item.Embedding)
                ?.ToList() ?? new List<List<float>>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取嵌入向量失败: {ex.Message}");
            // 返回零向量作为备选
            return Enumerable.Repeat(
                Enumerable.Repeat(0.0f, _embeddingDim).ToList(),
                processedTexts.Count).ToList();
        }
    }

    // 提取成员（方法/构造函数）体的辅助方法
    private (List<string> codeLines, int endLine) ExtractMemberBody(string[] lines, int startLine)
    {
        var memberBody = new List<string>();
        int j = startLine;
        int braceCount = 0;
        bool foundOpenBrace = false;
        
        // 首先添加方法签名行
        memberBody.Add(lines[startLine]);
        
        // 检查方法签名行是否包含开括号
        if (lines[startLine].Contains('{'))
        {
            braceCount = lines[startLine].Count(c => c == '{') - lines[startLine].Count(c => c == '}');
            foundOpenBrace = true;
        }
        
        j = startLine + 1;
        
        // 如果方法签名行没有开括号，寻找下一个开括号
        while (j < lines.Length && !foundOpenBrace)
        {
            memberBody.Add(lines[j]);
            if (lines[j].Contains('{'))
            {
                braceCount = lines[j].Count(c => c == '{') - lines[j].Count(c => c == '}');
                foundOpenBrace = true;
            }
            j++;
        }
        
        // 提取方法体直到找到匹配的闭括号
        while (j < lines.Length && braceCount > 0)
        {
            int openBraces = lines[j].Count(c => c == '{');
            int closeBraces = lines[j].Count(c => c == '}');
            braceCount += openBraces - closeBraces;
            
            memberBody.Add(lines[j]);
            
            if (braceCount == 0)
            {
                break;
            }
            
            j++;
        }
        
        return (memberBody, j);
    }

    // 提取简单成员（字段、简单属性等）的辅助方法
    private (List<string> codeLines, int endLine) ExtractSimpleMember(string[] lines, int startLine)
    {
        var memberLines = new List<string>();
        int j = startLine;
        
        // 添加当前行
        memberLines.Add(lines[startLine]);
        
        // 如果当前行以分号结尾，直接返回
        if (lines[startLine].TrimEnd().EndsWith(';'))
        {
            return (memberLines, startLine);
        }
        
        // 否则继续查找到分号或大括号结束
        j = startLine + 1;
        while (j < lines.Length)
        {
            memberLines.Add(lines[j]);
            
            var trimmedLine = lines[j].TrimEnd();
            if (trimmedLine.EndsWith(';') || trimmedLine.EndsWith('}'))
            {
                break;
            }
            
            j++;
        }
        
        return (memberLines, j);
    }

    private List<CodeSnippet> ExtractCSharpSnippets(string filePath)
    {
        var snippets = new List<CodeSnippet>();
        
        try
        {
            Console.WriteLine($"[DEBUG] 开始解析文件: {filePath}");
            var content = File.ReadAllText(filePath);
            Console.WriteLine($"[DEBUG] 文件内容长度: {content.Length} 字符");
            
            // 统一换行符处理
            content = content.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = content.Split('\n');
            Console.WriteLine($"[DEBUG] 文件行数: {lines.Length}");
            
            // C#类和方法的正则表达式模式
            var classPattern = new Regex(@"^\s*(?:\[[\w\s,=.()""\/\\\-]*\]\s*)*(?:(public|private|protected|internal)\s+)?(?:(sealed|abstract|static|partial)\s+)*class\s+(\w+)(?:<[\w\s,<>]*>)?(?:\s*:\s*[\w\s,<>\.]+)?\s*\{?", RegexOptions.IgnoreCase);
            var methodPattern = new Regex(@"^\s*(?:\[[\w\s,=.()""\/\\\-]*\]\s*)*(?:(public|private|protected|internal)\s+)?(?:(static|virtual|override|abstract|async)\s+)*(?:([\w<>\[\]?\.]+)\s+)?(\w+)(?:<[\w\s,<>]*>)?\s*\([^)]*\)\s*(?:where\s+[\w\s:<>,]*\s*)?\{?", RegexOptions.IgnoreCase);
            var constructorPattern = new Regex(@"^\s*(?:\[[\w\s,=.()""\/\\\-]*\]\s*)*(?:(public|private|protected|internal)\s+)?(\w+)\s*\([^)]*\)\s*(?::\s*(?:base|this)\s*\([^)]*\)\s*)?\{?", RegexOptions.IgnoreCase);
            var propertyPattern = new Regex(@"^\s*(?:\[[\w\s,=.()""\/\\\-]*\]\s*)*(?:(public|private|protected|internal)\s+)?(?:(static|virtual|override|abstract|readonly)\s+)*(?:([\w<>\[\]?\.]+)\s+)(\w+)\s*(?:\{\s*(?:get|set)|\s*=\s*)", RegexOptions.IgnoreCase);
            var fieldPattern = new Regex(@"^\s*(?:\[[\w\s,=.()""\/\\\-]*\]\s*)*(?:(public|private|protected|internal)\s+)?(?:(static|readonly|const)\s+)*(?:([\w<>\[\]?\.]+)\s+)(\w+)\s*(?:=|;)", RegexOptions.IgnoreCase);
            var eventPattern = new Regex(@"^\s*(?:\[[\w\s,=.()""\/\\\-]*\]\s*)*(?:(public|private|protected|internal)\s+)?(?:(static|virtual|override|abstract)\s+)*event\s+(?:([\w<>\[\]?\.]+)\s+)?(\w+)\s*(?:\{|\s*;)", RegexOptions.IgnoreCase);
            
            string? currentNamespace = null;
            string? currentClass = null;
            
            for (int i = 0; i < lines.Length; i++)
            {
                // 检测命名空间
                var namespaceMatch = Regex.Match(lines[i], @"^\s*namespace\s+([\w.]+)\s*[{;]?");
                if (namespaceMatch.Success)
                {
                    currentNamespace = namespaceMatch.Groups[1].Value;
                    Console.WriteLine($"[DEBUG] 找到命名空间: {currentNamespace} 在第 {i + 1} 行");
                    currentClass = null;
                    continue;
                }
                
                // 检测类定义
                var classMatch = classPattern.Match(lines[i]);
                if (classMatch.Success)
                {
                    currentClass = classMatch.Groups[3].Value;
                    Console.WriteLine($"[DEBUG] 找到类: {currentClass} 在第 {i + 1} 行");
                    continue;
                }
                
                // 检测各种类成员定义
                var methodMatch = methodPattern.Match(lines[i]);
                var constructorMatch = constructorPattern.Match(lines[i]);
                var propertyMatch = propertyPattern.Match(lines[i]);
                var fieldMatch = fieldPattern.Match(lines[i]);
                var eventMatch = eventPattern.Match(lines[i]);
                
                string? memberName = null;
                string memberType = "";
                bool hasBody = false;
                
                if (methodMatch.Success && currentClass != null)
                {
                    memberName = methodMatch.Groups[4].Value;
                    memberType = "方法";
                    hasBody = true;
                    Console.WriteLine($"[DEBUG] 找到方法: {currentClass}.{memberName} 在第 {i + 1} 行");
                }
                else if (constructorMatch.Success && currentClass != null &&
                         constructorMatch.Groups[2].Value == currentClass)
                {
                    memberName = constructorMatch.Groups[2].Value;
                    memberType = "构造函数";
                    hasBody = true;
                    Console.WriteLine($"[DEBUG] 找到构造函数: {currentClass}.{memberName} 在第 {i + 1} 行");
                }
                else if (propertyMatch.Success && currentClass != null)
                {
                    memberName = propertyMatch.Groups[4].Value;
                    memberType = "属性";
                    hasBody = lines[i].Contains('{');
                    Console.WriteLine($"[DEBUG] 找到属性: {currentClass}.{memberName} 在第 {i + 1} 行");
                }
                else if (fieldMatch.Success && currentClass != null)
                {
                    memberName = fieldMatch.Groups[4].Value;
                    memberType = "字段";
                    hasBody = false;
                    Console.WriteLine($"[DEBUG] 找到字段: {currentClass}.{memberName} 在第 {i + 1} 行");
                }
                else if (eventMatch.Success && currentClass != null)
                {
                    memberName = eventMatch.Groups[4].Value;
                    memberType = "事件";
                    hasBody = lines[i].Contains('{');
                    Console.WriteLine($"[DEBUG] 找到事件: {currentClass}.{memberName} 在第 {i + 1} 行");
                }
                
                if (memberName != null && currentClass != null)
                {
                    // 提取成员内容
                    var memberBody = hasBody ? ExtractMemberBody(lines, i) : ExtractSimpleMember(lines, i);
                    
                    // 创建代码片段信息
                    var snippet = new CodeSnippet
                    {
                        FilePath = filePath,
                        Namespace = currentNamespace,
                        ClassName = currentClass,
                        MethodName = $"{memberName} ({memberType})",
                        Code = string.Join("\n", memberBody.codeLines),
                        StartLine = i + 1,
                        EndLine = memberBody.endLine + 1
                    };
                    
                    Console.WriteLine($"[DEBUG] 创建代码片段: {currentNamespace}.{currentClass}.{memberName} ({memberType})");
                    Console.WriteLine($"[DEBUG] 代码长度: {snippet.Code.Length} 字符, 行范围: {snippet.StartLine}-{snippet.EndLine}");
                    snippets.Add(snippet);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DEBUG] 处理文件 {filePath} 失败:");
            Console.WriteLine($"[DEBUG] 异常类型: {ex.GetType().Name}");
            Console.WriteLine($"[DEBUG] 异常消息: {ex.Message}");
            Console.WriteLine($"[DEBUG] 堆栈跟踪: {ex.StackTrace}");
        }
        
        Console.WriteLine($"[DEBUG] 文件 {filePath} 解析完成，共提取 {snippets.Count} 个代码片段");
        return snippets;
    }

    private async Task BatchIndexSnippets(List<CodeSnippet> snippets, int batchSize = MAX_BATCH_SIZE)
    {
        Console.WriteLine($"[INFO] 开始批量索引 {snippets.Count} 个代码片段，批量大小: {batchSize}");
        
        for (int i = 0; i < snippets.Count; i += batchSize)
        {
            var batch = snippets.Skip(i).Take(batchSize).ToList();
            Console.WriteLine($"[INFO] 处理批次 {i / batchSize + 1}/{(snippets.Count + batchSize - 1) / batchSize}，包含 {batch.Count} 个片段");
            
            // 提取代码文本
            var codes = batch.Select(snippet => snippet.Code).ToList();
            
            try
            {
                // 生成嵌入向量
                var embeddings = await GetEmbeddings(codes);
                
                // 准备点数据
                var points = new List<PointStruct>();
                for (int j = 0; j < batch.Count; j++)
                {
                    var vector = embeddings[j].Select(v => (double)v).ToList();
                    var payload = new Dictionary<string, Value>
                    {
                        ["filePath"] = new Value { StringValue = batch[j].FilePath },
                        ["namespace"] = new Value { StringValue = batch[j].Namespace ?? "" },
                        ["className"] = new Value { StringValue = batch[j].ClassName ?? "" },
                        ["methodName"] = new Value { StringValue = batch[j].MethodName ?? "" },
                        ["code"] = new Value { StringValue = batch[j].Code },
                        ["startLine"] = new Value { IntegerValue = batch[j].StartLine },
                        ["endLine"] = new Value { IntegerValue = batch[j].EndLine }
                    };
                    
                    var vectorData = new Vector();
                    vectorData.Data.AddRange(vector.Select(v => (float)v));
                    
                    points.Add(new PointStruct
                    {
                        Id = new PointId { Num = (ulong)(i + j) },
                        Vectors = new Vectors { Vector = vectorData },
                        Payload = { payload }
                    });
                }
                
                // 上传到Qdrant
                await _client.UpsertAsync(_options.QdrantConfig.CollectionName, points);
                Console.WriteLine($"[INFO] 批次 {i / batchSize + 1} 索引完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 批次 {i / batchSize + 1} 索引失败: {ex.Message}");
                // 继续处理下一个批次
            }
        }
        
        Console.WriteLine($"[INFO] 批量索引完成，共处理 {snippets.Count} 个代码片段");
    }
}