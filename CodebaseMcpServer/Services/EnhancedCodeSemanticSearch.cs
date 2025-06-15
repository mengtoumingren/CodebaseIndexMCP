using System.Text;
using Microsoft.Extensions.Options; // Added for IOptions
using Qdrant.Client;
using Qdrant.Client.Grpc;
using CodebaseMcpServer.Models;
using CodebaseMcpServer.Services.Embedding;
using CodebaseMcpServer.Services.Parsing;

namespace CodebaseMcpServer.Services;

/// <summary>
/// 增强版代码语义搜索服务 - 支持多集合管理
/// </summary>
public class EnhancedCodeSemanticSearch : IDisposable
{
    private readonly string _qdrantHost;
    private readonly int _qdrantPort;
    private readonly QdrantClient _client;
    private readonly ILogger<EnhancedCodeSemanticSearch> _logger;
    private readonly EmbeddingProviderFactory _embeddingProviderFactory;
    private IEmbeddingProvider? _defaultEmbeddingProvider; // Cache for default provider

    // 文本处理常量 - 这些可以保留，因为它们是通用的
    private const int APPROX_CHARS_PER_TOKEN = 4;

    public EnhancedCodeSemanticSearch(
        IOptions<CodeSearchOptions> codeSearchOptions,
        EmbeddingProviderFactory embeddingProviderFactory,
        ILogger<EnhancedCodeSemanticSearch>? logger = null)
    {
        var config = codeSearchOptions?.Value ?? throw new ArgumentNullException(nameof(codeSearchOptions), "CodeSearchOptions configuration is missing.");
        var qdrantConfig = config.QdrantConfig ?? throw new ArgumentNullException(nameof(config.QdrantConfig), "QdrantConfig section is missing in CodeSearchOptions.");
        
        _qdrantHost = qdrantConfig.Host;
        _qdrantPort = qdrantConfig.Port;
        
        _embeddingProviderFactory = embeddingProviderFactory ?? throw new ArgumentNullException(nameof(embeddingProviderFactory));
        _logger = logger ?? CreateNullLogger();
        
        _logger.LogDebug("连接Qdrant服务器: {Host}:{Port}", _qdrantHost, _qdrantPort);
        _client = new QdrantClient(_qdrantHost, _qdrantPort);
    }

    private IEmbeddingProvider GetDefaultProvider()
    {
        _defaultEmbeddingProvider ??= _embeddingProviderFactory.GetDefaultProvider();
        return _defaultEmbeddingProvider;
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
                        Size = (ulong)GetDefaultProvider().GetEmbeddingDimension(), // Use provider's dimension
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

    // Old GetEmbeddings method has been removed.

    /// <summary>
    /// 提取代码片段 - 使用多语言解析器框架
    /// </summary>
    public List<CodeSnippet> ExtractCodeSnippets(string filePath)
    {
        try
        {
            _logger.LogDebug("开始解析文件: {FilePath}", filePath);
            
            // 使用新的解析器工厂
            var parser = CodeParserFactory.GetParser(filePath);
            if (parser == null)
            {
                _logger.LogWarning("不支持的文件类型: {FilePath}", filePath);
                return new List<CodeSnippet>();
            }
            
            var snippets = parser.ParseCodeFile(filePath);
            
            _logger.LogDebug("文件 {FilePath} 解析完成，语言: {Language}，提取 {Count} 个代码片段",
                filePath, parser.Language, snippets.Count);
            
            return snippets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析文件失败: {FilePath}", filePath);
            return new List<CodeSnippet>();
        }
    }
    
    /// <summary>
    /// 提取C#代码片段 - 向后兼容方法
    /// </summary>
    public List<CodeSnippet> ExtractCSharpSnippets(string filePath)
    {
        return ExtractCodeSnippets(filePath);
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
                var snippets = ExtractCodeSnippets(filePath);
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
    public async Task BatchIndexSnippetsAsync(List<CodeSnippet> snippets, string collectionName)
    {
        var embeddingProvider = GetDefaultProvider();
        int providerBatchSize = embeddingProvider.GetMaxBatchSize();
        int maxTokenLength = embeddingProvider.GetMaxTokenLength();
        int expectedDimension = embeddingProvider.GetEmbeddingDimension();

        _logger.LogInformation("开始批量索引 {Count} 个代码片段到集合 {CollectionName} 使用 {ProviderName}",
            snippets.Count, collectionName, embeddingProvider.ProviderName);
        
        for (int i = 0; i < snippets.Count; i += providerBatchSize)
        {
            var batch = snippets.Skip(i).Take(providerBatchSize).ToList();
            _logger.LogDebug("处理批次 {BatchNum}/{TotalBatches}，包含 {Count} 个片段",
                i / providerBatchSize + 1, (snippets.Count + providerBatchSize - 1) / providerBatchSize, batch.Count);
            
            var processedCodes = new List<string>();
            var originalBatchItems = new List<CodeSnippet>(); // To keep track of snippets corresponding to processedCodes

            foreach(var snippet in batch)
            {
                var text = snippet.Code;
                
                // 检查代码片段是否为空或仅包含空白字符
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("跳过空代码片段来自 {FilePath} (行 {StartLine}-{EndLine})",
                        snippet.FilePath, snippet.StartLine, snippet.EndLine);
                    continue;
                }
                
                var estimatedTokens = EstimateTokenCount(text);
                if (estimatedTokens > maxTokenLength)
                {
                    _logger.LogWarning("代码片段来自 {FilePath} (行 {StartLine}-{EndLine}) 长度过长 (约{Tokens}个Token)，将被截断至 {MaxTokens} Tokens.",
                        snippet.FilePath, snippet.StartLine, snippet.EndLine, estimatedTokens, maxTokenLength);
                    text = TruncateText(text, maxTokenLength);
                }
                
                // 截断后再次检查是否为空
                if (string.IsNullOrWhiteSpace(text))
                {
                    _logger.LogWarning("截断后代码片段变为空，跳过来自 {FilePath} (行 {StartLine}-{EndLine})",
                        snippet.FilePath, snippet.StartLine, snippet.EndLine);
                    continue;
                }
                
                processedCodes.Add(text);
                originalBatchItems.Add(snippet); // Keep original snippet for payload
            }
            
            if (!processedCodes.Any())
            {
                _logger.LogWarning("批次 {BatchNum} 没有可处理的代码片段（全部为空或无效）。", i / providerBatchSize + 1);
                continue;
            }
            
            // 添加详细日志来帮助诊断
            _logger.LogDebug("批次 {BatchNum} 处理完成: 原始片段数={OriginalCount}, 有效片段数={ValidCount}",
                i / providerBatchSize + 1, batch.Count, processedCodes.Count);

            try
            {
                var embeddings = await embeddingProvider.GetEmbeddingsAsync(processedCodes);
                if (embeddings.Count != processedCodes.Count)
                {
                    _logger.LogError("批次 {BatchNum} 索引失败: 获取到的嵌入向量数量 ({EmbeddingsCount}) 与处理后的代码片段数量 ({ProcessedCount}) 不匹配。",
                        i / providerBatchSize + 1, embeddings.Count, processedCodes.Count);
                    continue;
                }

                var points = new List<PointStruct>();
                for (int j = 0; j < embeddings.Count; j++)
                {
                    if (embeddings[j].Count != expectedDimension)
                    {
                         _logger.LogWarning("批次 {BatchNum}, 片段 {SnippetIndex}: 嵌入向量维度 ({ActualDim}) 与提供商声明的维度 ({ExpectedDim}) 不符。跳过此片段。",
                            i / providerBatchSize + 1, j, embeddings[j].Count, expectedDimension);
                        continue;
                    }

                    var currentSnippet = originalBatchItems[j]; // Use the original snippet for payload
                    var payload = new Dictionary<string, Value>
                    {
                        ["filePath"] = new Value { StringValue = currentSnippet.FilePath },
                        ["namespace"] = new Value { StringValue = currentSnippet.Namespace ?? "" },
                        ["className"] = new Value { StringValue = currentSnippet.ClassName ?? "" },
                        ["methodName"] = new Value { StringValue = currentSnippet.MethodName ?? "" },
                        ["code"] = new Value { StringValue = currentSnippet.Code }, // Store original, untruncated code
                        ["startLine"] = new Value { IntegerValue = currentSnippet.StartLine },
                        ["endLine"] = new Value { IntegerValue = currentSnippet.EndLine }
                    };
                    
                    var vectorData = new Vector();
                    vectorData.Data.AddRange(embeddings[j]); // Embeddings are already float
                    
                    points.Add(new PointStruct
                    {
                        Id = new PointId { Uuid = Guid.NewGuid().ToString() },
                        Vectors = new Vectors { Vector = vectorData },
                        Payload = { payload }
                    });
                }
                
                if (points.Any())
                {
                    await _client.UpsertAsync(collectionName, points);
                    _logger.LogDebug("批次 {BatchNum} 索引完成，成功索引 {PointCount} 个点。", i / providerBatchSize + 1, points.Count);
                }
                else
                {
                    _logger.LogWarning("批次 {BatchNum} 没有可索引的点。", i / providerBatchSize + 1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批次 {BatchNum} 索引失败", i / providerBatchSize + 1);
            }
        }
        
        _logger.LogInformation("批量索引完成，共处理 {Count} 个代码片段", snippets.Count);
    }

    /// <summary>
    /// 在指定集合中搜索
    /// </summary>
    public async Task<List<SearchResult>> SearchAsync(string query, string collectionName, int limit = 5)
    {
        var embeddingProvider = GetDefaultProvider();
        var processedQuery = TruncateText(query, embeddingProvider.GetMaxTokenLength());
        
        List<List<float>> queryEmbeddingLists;
        try
        {
            queryEmbeddingLists = await embeddingProvider.GetEmbeddingsAsync(new List<string> { processedQuery });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取查询 '{Query}' 的嵌入向量失败，使用提供商 {ProviderName}", query, embeddingProvider.ProviderName);
            return new List<SearchResult>();
        }
        
        if (queryEmbeddingLists == null || queryEmbeddingLists.Count == 0 || queryEmbeddingLists[0].Count == 0)
        {
            _logger.LogError("无法获取查询 '{Query}' 的嵌入向量 (结果为空)，使用提供商 {ProviderName}", query, embeddingProvider.ProviderName);
            return new List<SearchResult>();
        }
        var queryVectorData = queryEmbeddingLists[0].ToArray();
        
        if (queryVectorData.Length != embeddingProvider.GetEmbeddingDimension())
        {
            _logger.LogError("查询向量维度 ({QueryDim}) 与提供商声明的维度 ({ProviderDim}) 不符。无法执行搜索。",
                queryVectorData.Length, embeddingProvider.GetEmbeddingDimension());
            return new List<SearchResult>();
        }

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
    /// 删除指定文件的所有索引点
    /// </summary>
    public async Task<bool> DeleteFileIndexAsync(string filePath, string collectionName)
    {
        try
        {
            _logger.LogDebug("开始删除文件索引: {FilePath} from {CollectionName}", filePath, collectionName);
            
            // 使用 Delete 方法按条件删除点
            // 对文件路径中的反斜杠进行转义，以确保在Qdrant中正确匹配
            string escapedFilePath = filePath.Replace("\\", "\\\\");
            _logger.LogDebug("Escaped file path for Qdrant filter: {EscapedFilePath}", escapedFilePath);

            var deleteResult = await _client.DeleteAsync(collectionName, new Filter
            {
                Must = {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "filePath",
                            Match = new Qdrant.Client.Grpc.Match { Text = escapedFilePath }
                        }
                    }
                }
            });

            _logger.LogInformation("成功删除文件 {FilePath} 的索引点，操作ID: {OperationId}", filePath, deleteResult.OperationId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除文件索引失败: {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _client?.Dispose();
    }
}

// Removed old EmbeddingResponse and EmbeddingData models as they are now in Services/Embedding/Models