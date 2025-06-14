using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using CodebaseMcpServer.Models;

namespace CodebaseMcpServer.Services;

/// <summary>
/// 增强版代码语义搜索服务 - 支持多集合管理
/// </summary>
public class EnhancedCodeSemanticSearch
{
    private readonly string _apiKey;
    private readonly string _qdrantHost;
    private readonly int _qdrantPort;
    private readonly int _embeddingDim = 1024;
    private readonly QdrantClient _client;
    private readonly ILogger<EnhancedCodeSemanticSearch> _logger;
    
    // API限制常量
    private const int MAX_BATCH_SIZE = 10;
    private const int MAX_TOKEN_LENGTH = 8192;
    private const int APPROX_CHARS_PER_TOKEN = 4;

    public EnhancedCodeSemanticSearch(
        string apiKey,
        string qdrantHost = "localhost",
        int qdrantPort = 6334,
        ILogger<EnhancedCodeSemanticSearch>? logger = null)
    {
        _apiKey = apiKey;
        _qdrantHost = qdrantHost;
        _qdrantPort = qdrantPort;
        _logger = logger ?? CreateNullLogger();
        
        _logger.LogDebug("连接Qdrant服务器: {Host}:{Port}", _qdrantHost, _qdrantPort);
        _client = new QdrantClient(_qdrantHost, _qdrantPort);
    }

    private static ILogger<EnhancedCodeSemanticSearch> CreateNullLogger()
    {
        using var factory = LoggerFactory.Create(builder => builder.AddConsole());
        return factory.CreateLogger<EnhancedCodeSemanticSearch>();
    }

    /// <summary>
    /// 确保集合存在
    /// </summary>
    public async Task<bool> EnsureCollectionAsync(string collectionName)
    {
        try
        {
            _logger.LogDebug("检查集合是否存在: {CollectionName}", collectionName);
            await _client.GetCollectionInfoAsync(collectionName);
            _logger.LogDebug("集合 {CollectionName} 已存在", collectionName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("集合不存在或连接失败: {Message}", ex.Message);
            
            try
            {
                _logger.LogDebug("尝试创建新集合: {CollectionName}", collectionName);
                await _client.CreateCollectionAsync(
                    collectionName,
                    new VectorParams
                    {
                        Size = (ulong)_embeddingDim,
                        Distance = Distance.Cosine
                    });
                _logger.LogInformation("集合 {CollectionName} 创建成功", collectionName);
                return true;
            }
            catch (Exception createEx)
            {
                _logger.LogError(createEx, "创建集合失败: {CollectionName}", collectionName);
                return false;
            }
        }
    }

    /// <summary>
    /// 估算文本的Token数量
    /// </summary>
    private int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
            
        var wordCount = text.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var chineseCharCount = text.Count(c => c >= 0x4e00 && c <= 0x9fff);
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
            
        var lines = text.Split('\n');
        var result = new StringBuilder();
        var currentTokens = 0;
        
        foreach (var line in lines)
        {
            var lineTokens = EstimateTokenCount(line + "\n");
            if (currentTokens + lineTokens > maxTokens)
                break;
                
            result.AppendLine(line);
            currentTokens += lineTokens;
        }
        
        var truncated = result.ToString().TrimEnd();
        _logger.LogWarning("文本从 {OriginalTokens} Token 截断至 {TruncatedTokens} Token", 
            EstimateTokenCount(text), EstimateTokenCount(truncated));
        
        return truncated;
    }

    /// <summary>
    /// 获取文本嵌入向量
    /// </summary>
    private async Task<List<List<float>>> GetEmbeddings(List<string> texts)
    {
        if (texts.Count > MAX_BATCH_SIZE)
        {
            throw new ArgumentException($"批量大小不能超过 {MAX_BATCH_SIZE}，当前为 {texts.Count}");
        }
        
        var processedTexts = new List<string>();
        for (int i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            var estimatedTokens = EstimateTokenCount(text);
            
            if (estimatedTokens > MAX_TOKEN_LENGTH)
            {
                _logger.LogWarning("文本 {Index} 长度过长 (约{Tokens}个Token)，将被截断", i, estimatedTokens);
                text = TruncateText(text, MAX_TOKEN_LENGTH);
            }
            
            processedTexts.Add(text);
        }
        
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
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
            
            return embeddingResponse?.Data?.Select(item => item.Embedding)?.ToList() ?? new List<List<float>>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取嵌入向量失败");
            return Enumerable.Repeat(
                Enumerable.Repeat(0.0f, _embeddingDim).ToList(),
                processedTexts.Count).ToList();
        }
    }

    /// <summary>
    /// 提取C#代码片段
    /// </summary>
    public List<CodeSnippet> ExtractCSharpSnippets(string filePath)
    {
        var snippets = new List<CodeSnippet>();
        
        try
        {
            _logger.LogDebug("开始解析文件: {FilePath}", filePath);
            var content = File.ReadAllText(filePath);
            
            content = content.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = content.Split('\n');
            
            var classPattern = new Regex(@"^\s*(?:\[[\w\s,=.()""\/\\\-]*\]\s*)*(?:(public|private|protected|internal)\s+)?(?:(sealed|abstract|static|partial)\s+)*class\s+(\w+)(?:<[\w\s,<>]*>)?(?:\s*:\s*[\w\s,<>\.]+)?\s*\{?", RegexOptions.IgnoreCase);
            var methodPattern = new Regex(@"^\s*(?:\[[\w\s,=.()""\/\\\-]*\]\s*)*(?:(public|private|protected|internal)\s+)?(?:(static|virtual|override|abstract|async)\s+)*(?:([\w<>\[\]?\.]+)\s+)?(\w+)(?:<[\w\s,<>]*>)?\s*\([^)]*\)\s*(?:where\s+[\w\s:<>,]*\s*)?\{?", RegexOptions.IgnoreCase);
            var constructorPattern = new Regex(@"^\s*(?:\[[\w\s,=.()""\/\\\-]*\]\s*)*(?:(public|private|protected|internal)\s+)?(\w+)\s*\([^)]*\)\s*(?::\s*(?:base|this)\s*\([^)]*\)\s*)?\{?", RegexOptions.IgnoreCase);
            var propertyPattern = new Regex(@"^\s*(?:\[[\w\s,=.()""\/\\\-]*\]\s*)*(?:(public|private|protected|internal)\s+)?(?:(static|virtual|override|abstract|readonly)\s+)*(?:([\w<>\[\]?\.]+)\s+)(\w+)\s*(?:\{\s*(?:get|set)|\s*=\s*)", RegexOptions.IgnoreCase);
            var fieldPattern = new Regex(@"^\s*(?:\[[\w\s,=.()""\/\\\-]*\]\s*)*(?:(public|private|protected|internal)\s+)?(?:(static|readonly|const)\s+)*(?:([\w<>\[\]?\.]+)\s+)(\w+)\s*(?:=|;)", RegexOptions.IgnoreCase);
            
            string? currentNamespace = null;
            string? currentClass = null;
            
            for (int i = 0; i < lines.Length; i++)
            {
                // 检测命名空间
                var namespaceMatch = Regex.Match(lines[i], @"^\s*namespace\s+([\w.]+)\s*[{;]?");
                if (namespaceMatch.Success)
                {
                    currentNamespace = namespaceMatch.Groups[1].Value;
                    currentClass = null;
                    continue;
                }
                
                // 检测类定义
                var classMatch = classPattern.Match(lines[i]);
                if (classMatch.Success)
                {
                    currentClass = classMatch.Groups[3].Value;
                    continue;
                }
                
                // 检测各种类成员
                var methodMatch = methodPattern.Match(lines[i]);
                var constructorMatch = constructorPattern.Match(lines[i]);
                var propertyMatch = propertyPattern.Match(lines[i]);
                var fieldMatch = fieldPattern.Match(lines[i]);
                
                string? memberName = null;
                string memberType = "";
                bool hasBody = false;
                
                if (methodMatch.Success && currentClass != null)
                {
                    memberName = methodMatch.Groups[4].Value;
                    memberType = "方法";
                    hasBody = true;
                }
                else if (constructorMatch.Success && currentClass != null &&
                         constructorMatch.Groups[2].Value == currentClass)
                {
                    memberName = constructorMatch.Groups[2].Value;
                    memberType = "构造函数";
                    hasBody = true;
                }
                else if (propertyMatch.Success && currentClass != null)
                {
                    memberName = propertyMatch.Groups[4].Value;
                    memberType = "属性";
                    hasBody = lines[i].Contains('{');
                }
                else if (fieldMatch.Success && currentClass != null)
                {
                    memberName = fieldMatch.Groups[4].Value;
                    memberType = "字段";
                    hasBody = false;
                }
                
                if (memberName != null && currentClass != null)
                {
                    var memberBody = hasBody ? ExtractMemberBody(lines, i) : ExtractSimpleMember(lines, i);
                    
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
                    
                    snippets.Add(snippet);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理文件失败: {FilePath}", filePath);
        }
        
        _logger.LogDebug("文件 {FilePath} 解析完成，提取 {Count} 个代码片段", filePath, snippets.Count);
        return snippets;
    }

    private (List<string> codeLines, int endLine) ExtractMemberBody(string[] lines, int startLine)
    {
        var memberBody = new List<string>();
        int j = startLine;
        int braceCount = 0;
        bool foundOpenBrace = false;
        
        memberBody.Add(lines[startLine]);
        
        if (lines[startLine].Contains('{'))
        {
            braceCount = lines[startLine].Count(c => c == '{') - lines[startLine].Count(c => c == '}');
            foundOpenBrace = true;
        }
        
        j = startLine + 1;
        
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
        
        while (j < lines.Length && braceCount > 0)
        {
            int openBraces = lines[j].Count(c => c == '{');
            int closeBraces = lines[j].Count(c => c == '}');
            braceCount += openBraces - closeBraces;
            
            memberBody.Add(lines[j]);
            
            if (braceCount == 0)
                break;
                
            j++;
        }
        
        return (memberBody, j);
    }

    private (List<string> codeLines, int endLine) ExtractSimpleMember(string[] lines, int startLine)
    {
        var memberLines = new List<string>();
        int j = startLine;
        
        memberLines.Add(lines[startLine]);
        
        if (lines[startLine].TrimEnd().EndsWith(';'))
        {
            return (memberLines, startLine);
        }
        
        j = startLine + 1;
        while (j < lines.Length)
        {
            memberLines.Add(lines[j]);
            
            var trimmedLine = lines[j].TrimEnd();
            if (trimmedLine.EndsWith(';') || trimmedLine.EndsWith('}'))
                break;
                
            j++;
        }
        
        return (memberLines, j);
    }

    /// <summary>
    /// 处理代码库并建立索引
    /// </summary>
    public async Task<int> ProcessCodebaseAsync(string codebasePath, string collectionName, List<string>? filePatterns = null)
    {
        filePatterns ??= new List<string> { "*.cs" };
        var allSnippets = new List<CodeSnippet>();
        
        // 确保集合存在
        if (!await EnsureCollectionAsync(collectionName))
        {
            throw new InvalidOperationException($"无法创建或访问集合: {collectionName}");
        }
        
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
        await BatchIndexSnippetsAsync(allSnippets, collectionName);
        
        return allSnippets.Count;
    }

    /// <summary>
    /// 批量索引代码片段
    /// </summary>
    public async Task BatchIndexSnippetsAsync(List<CodeSnippet> snippets, string collectionName, int batchSize = MAX_BATCH_SIZE)
    {
        _logger.LogInformation("开始批量索引 {Count} 个代码片段到集合 {CollectionName}", snippets.Count, collectionName);
        
        for (int i = 0; i < snippets.Count; i += batchSize)
        {
            var batch = snippets.Skip(i).Take(batchSize).ToList();
            _logger.LogDebug("处理批次 {BatchNum}/{TotalBatches}，包含 {Count} 个片段", 
                i / batchSize + 1, (snippets.Count + batchSize - 1) / batchSize, batch.Count);
            
            var codes = batch.Select(snippet => snippet.Code).ToList();
            
            try
            {
                var embeddings = await GetEmbeddings(codes);
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
                
                await _client.UpsertAsync(collectionName, points);
                _logger.LogDebug("批次 {BatchNum} 索引完成", i / batchSize + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批次 {BatchNum} 索引失败", i / batchSize + 1);
            }
        }
        
        _logger.LogInformation("批量索引完成，共处理 {Count} 个代码片段", snippets.Count);
    }

    /// <summary>
    /// 在指定集合中搜索
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string query, string collectionName, int limit = 5)
    {
        var queryEmbedding = await GetEmbeddings(new List<string> { query });
        var queryVectorData = queryEmbedding[0].ToArray();
        
        var searchResult = await _client.SearchAsync(
            collectionName,
            queryVectorData,
            limit: (ulong)limit);
        
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

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _client?.Dispose();
    }
}

// API响应模型（重用现有的）
public class EmbeddingResponse
{
    [JsonProperty("data")]
    public List<EmbeddingData> Data { get; set; } = new();
}

public class EmbeddingData
{
    [JsonProperty("embedding")]
    public List<float> Embedding { get; set; } = new();
}