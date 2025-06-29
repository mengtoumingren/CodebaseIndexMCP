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
    /// 按批次处理代码库并建立索引 - 流式处理模式
    /// </summary>
    /// <param name="codebasePath">代码库路径</param>
    /// <param name="collectionName">集合名称</param>
    /// <param name="filePatterns">文件模式列表</param>
    /// <param name="batchSize">批处理大小（文件数量）</param>
    /// <param name="progressCallback">进度回调函数</param>
    /// <returns>总共索引的代码片段数量</returns>
    public async Task<int> ProcessCodebaseInBatchesAsync(
        string codebasePath,
        string collectionName,
        List<string>? filePatterns = null,
        int batchSize = 10,
        Func<int, int, string, Task>? progressCallback = null)
    {
        filePatterns ??= new List<string> { "*.cs" };
        
        // 确保集合存在
        if (!await EnsureCollectionAsync(collectionName))
        {
            throw new InvalidOperationException($"无法创建或访问集合: {collectionName}");
        }
        
        // 获取所有匹配的文件
        var allFiles = new List<string>();
        foreach (var pattern in filePatterns)
        {
            allFiles.AddRange(Directory.GetFiles(codebasePath, pattern, SearchOption.AllDirectories));
        }
        
        var totalFiles = allFiles.Count;
        var totalSnippets = 0;
        var processedFiles = 0;
        
        _logger.LogInformation("开始批处理索引：{TotalFiles} 个文件，批大小：{BatchSize}",
            totalFiles, batchSize);
        
        // 按批次处理文件
        for (int i = 0; i < allFiles.Count; i += batchSize)
        {
            var batch = allFiles.Skip(i).Take(batchSize).ToList();
            var batchNumber = i / batchSize + 1;
            var totalBatches = (totalFiles + batchSize - 1) / batchSize;
            
            _logger.LogDebug("处理批次 {BatchNumber}/{TotalBatches}，包含 {FileCount} 个文件",
                batchNumber, totalBatches, batch.Count);
            
            try
            {
                // 处理当前批次的文件
                var batchSnippets = new List<CodeSnippet>();
                
                foreach (var filePath in batch)
                {
                    // 更新进度回调
                    if (progressCallback != null)
                    {
                        await progressCallback(processedFiles, totalFiles, Path.GetFileName(filePath));
                    }
                    
                    var snippets = ExtractCodeSnippets(filePath);
                    batchSnippets.AddRange(snippets);
                    processedFiles++;
                    
                    _logger.LogTrace("文件 {FileName} 解析完成，提取 {Count} 个代码片段",
                        Path.GetFileName(filePath), snippets.Count);
                }
                
                // 立即索引当前批次的代码片段
                if (batchSnippets.Any())
                {
                    await BatchIndexSnippetsAsync(batchSnippets, collectionName);
                    totalSnippets += batchSnippets.Count;
                    
                    _logger.LogInformation("批次 {BatchNumber}/{TotalBatches} 索引完成：{SnippetCount} 个代码片段",
                        batchNumber, totalBatches, batchSnippets.Count);
                }
                else
                {
                    _logger.LogWarning("批次 {BatchNumber}/{TotalBatches} 没有提取到代码片段",
                        batchNumber, totalBatches);
                }
                
                // 释放内存
                batchSnippets.Clear();
                
                // 调用最终进度回调
                if (progressCallback != null)
                {
                    await progressCallback(processedFiles, totalFiles, $"批次 {batchNumber} 完成");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批次 {BatchNumber}/{TotalBatches} 处理失败，跳过继续处理下一批次",
                    batchNumber, totalBatches);
                
                // 更新已处理文件数，即使失败也要继续
                processedFiles += batch.Count;
            }
        }
        
        _logger.LogInformation("批处理索引完成：共处理 {TotalFiles} 个文件，索引 {TotalSnippets} 个代码片段",
            totalFiles, totalSnippets);
        
        return totalSnippets;
    }

    /// <summary>
    /// 处理代码库并建立索引 - 传统方法（向后兼容）
    /// </summary>
    public async Task<int> ProcessCodebaseAsync(string codebasePath, string collectionName, List<string>? filePatterns = null)
    {
        // 调用新的批处理方法，使用默认批大小
        return await ProcessCodebaseInBatchesAsync(codebasePath, collectionName, filePatterns, batchSize: 50);
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
    /// 并发批量索引代码片段 - 增强版
    /// </summary>
    public async Task<int> BatchIndexSnippetsConcurrentlyAsync(
        List<CodeSnippet> snippets, 
        string collectionName,
        ConcurrencySettings? concurrencySettings = null)
    {
        var settings = concurrencySettings ?? GetDefaultConcurrencySettings();
        
        using var concurrentManager = new ConcurrentEmbeddingManager(
            GetDefaultProvider(), settings,
            LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ConcurrentEmbeddingManager>());
        
        _logger.LogInformation("开始并发批量索引 {Count} 个代码片段到集合 {CollectionName}",
            snippets.Count, collectionName);
        
        // 按并发批次分组
        var concurrentBatches = SplitSnippetsForConcurrentProcessing(snippets, settings);
        var indexedCount = 0;
        
        // 并发处理多个批次
        var concurrencyLimiter = new SemaphoreSlim(
            settings.MaxConcurrentFileBatches, 
            settings.MaxConcurrentFileBatches);
        
        var tasks = concurrentBatches.Select(async batch =>
        {
            await concurrencyLimiter.WaitAsync();
            try
            {
                var batchIndexed = await ProcessSnippetBatchConcurrently(
                    batch, collectionName, concurrentManager);
                Interlocked.Add(ref indexedCount, batchIndexed);
                return batchIndexed;
            }
            finally
            {
                concurrencyLimiter.Release();
            }
        });
        
        await Task.WhenAll(tasks);
        
        _logger.LogInformation("并发批量索引完成，共处理 {Count} 个代码片段", indexedCount);
        return indexedCount;
    }

    /// <summary>
    /// 获取默认并发设置
    /// </summary>
    private ConcurrencySettings GetDefaultConcurrencySettings()
    {
        return new ConcurrencySettings();
    }

    /// <summary>
    /// 分割代码片段为并发处理批次
    /// </summary>
    private List<List<CodeSnippet>> SplitSnippetsForConcurrentProcessing(
        List<CodeSnippet> snippets, 
        ConcurrencySettings settings)
    {
        var embeddingProvider = GetDefaultProvider();
        var optimalBatchSize = Math.Min(
            embeddingProvider.GetMaxBatchSize(), 
            settings.EmbeddingBatchSizeOptimal);
        
        var batches = new List<List<CodeSnippet>>();
        for (int i = 0; i < snippets.Count; i += optimalBatchSize)
        {
            var batch = snippets.Skip(i).Take(optimalBatchSize).ToList();
            batches.Add(batch);
        }
        
        _logger.LogDebug("代码片段分为 {BatchCount} 个并发批次，每批最多 {BatchSize} 个片段",
            batches.Count, optimalBatchSize);
        
        return batches;
    }

    /// <summary>
    /// 处理单个代码片段批次（并发）
    /// </summary>
    private async Task<int> ProcessSnippetBatchConcurrently(
        List<CodeSnippet> batch,
        string collectionName,
        ConcurrentEmbeddingManager concurrentManager)
    {
        var embeddingProvider = GetDefaultProvider();
        var maxTokenLength = embeddingProvider.GetMaxTokenLength();
        var expectedDimension = embeddingProvider.GetEmbeddingDimension();
        
        // 过滤和预处理代码片段
        var validSnippets = new List<CodeSnippet>();
        var validTexts = new List<string>();
        
        foreach (var snippet in batch)
        {
            if (string.IsNullOrWhiteSpace(snippet.Code)) continue;
            
            var processedCode = PreprocessCodeText(snippet.Code, maxTokenLength);
            if (string.IsNullOrWhiteSpace(processedCode)) continue;
            
            validSnippets.Add(snippet);
            validTexts.Add(processedCode);
        }
        
        if (!validTexts.Any()) return 0;
        
        try
        {
            // 并发获取嵌入向量
            var embeddings = await concurrentManager.GetEmbeddingsConcurrentlyAsync(validTexts);
            
            if (embeddings.Count != validTexts.Count)
            {
                _logger.LogWarning("嵌入向量数量不匹配：期望 {Expected}，实际 {Actual}",
                    validTexts.Count, embeddings.Count);
                return 0;
            }
            
            // 构建索引点
            var points = BuildIndexPoints(validSnippets, embeddings, expectedDimension);
            
            // 批量插入 Qdrant
            if (points.Any())
            {
                await _client.UpsertAsync(collectionName, points);
                _logger.LogDebug("批次索引完成：{PointCount} 个索引点", points.Count);
                return points.Count;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批次并发索引失败：{BatchSize} 个代码片段", batch.Count);
        }
        
        return 0;
    }

    /// <summary>
    /// 预处理代码文本
    /// </summary>
    private string PreprocessCodeText(string code, int maxTokenLength)
    {
        var estimatedTokens = EstimateTokenCount(code);
        
        if (estimatedTokens > maxTokenLength)
        {
            _logger.LogDebug("代码片段过长 (约{Tokens}个Token)，截断至 {MaxTokens} Token",
                estimatedTokens, maxTokenLength);
            return TruncateText(code, maxTokenLength);
        }
        
        return code;
    }

    /// <summary>
    /// 构建索引点
    /// </summary>
    private List<PointStruct> BuildIndexPoints(
        List<CodeSnippet> snippets, 
        List<List<float>> embeddings,
        int expectedDimension)
    {
        var points = new List<PointStruct>();
        
        for (int i = 0; i < snippets.Count && i < embeddings.Count; i++)
        {
            var snippet = snippets[i];
            var embedding = embeddings[i];
            
            if (embedding.Count != expectedDimension)
            {
                _logger.LogWarning("嵌入向量维度不匹配：期望 {Expected}，实际 {Actual}，跳过片段",
                    expectedDimension, embedding.Count);
                continue;
            }
            
            var payload = new Dictionary<string, Value>
            {
                ["filePath"] = new Value { StringValue = snippet.FilePath },
                ["namespace"] = new Value { StringValue = snippet.Namespace ?? "" },
                ["className"] = new Value { StringValue = snippet.ClassName ?? "" },
                ["methodName"] = new Value { StringValue = snippet.MethodName ?? "" },
                ["code"] = new Value { StringValue = snippet.Code },
                ["startLine"] = new Value { IntegerValue = snippet.StartLine },
                ["endLine"] = new Value { IntegerValue = snippet.EndLine }
            };
            
            var vectorData = new Vector();
            vectorData.Data.AddRange(embedding);
            
            points.Add(new PointStruct
            {
                Id = new PointId { Uuid = Guid.NewGuid().ToString() },
                Vectors = new Vectors { Vector = vectorData },
                Payload = { payload }
            });
        }
        
        return points;
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
            var deleteResult = await _client.DeleteAsync(collectionName, new Filter
            {
                Must = {
                    new Condition
                    {
                        Field = new FieldCondition
                        {
                            Key = "filePath",
                            Match = new Qdrant.Client.Grpc.Match { Text = filePath }
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
    /// 删除整个集合
    /// </summary>
    public async Task<bool> DeleteCollectionAsync(string collectionName)
    {
        try
        {
            _logger.LogInformation("开始删除 Qdrant 集合: {CollectionName}", collectionName);
            await _client.DeleteCollectionAsync(collectionName);
            _logger.LogInformation("成功删除 Qdrant 集合: {CollectionName}", collectionName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除 Qdrant 集合失败: {CollectionName}", collectionName);
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