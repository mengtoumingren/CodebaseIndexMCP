using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Codebase.Parsing;

namespace CodeSearch
{
    // 表示一个代码片段
    public class CodeSnippet
    {
        public string FilePath { get; set; } = string.Empty;
        public string? Namespace { get; set; }
        public string? ClassName { get; set; }
        public string? MethodName { get; set; }
        public string Code { get; set; } = string.Empty;
        public int StartLine { get; set; }
        public int EndLine { get; set; }
    }

    // 表示搜索结果
    public class SearchResult
    {
        public float Score { get; set; }
        public CodeSnippet Snippet { get; set; } = new();
    }

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
    /// 代码语义搜索主类
    ///
    /// 优化说明：
    /// 1. 严格遵循DashScope API限制：
    ///    - text-embedding-v4模型批量最多10条文本
    ///    - 每条文本最长8,192 Token
    /// 2. 智能文本截断：保持代码结构完整性
    /// 3. Token估算：结合中英文特点进行估算
    /// 4. 错误处理：批量处理中单个失败不影响整体流程
    /// </summary>
    public class CodeSemanticSearch
    {
        private readonly string _apiKey;
        private readonly string _qdrantHost;
        private readonly int _qdrantPort;
        private readonly string _collectionName;
        private readonly int _embeddingDim = 1024;
        private readonly QdrantClient _client;
        
        // API限制常量
        private const int MAX_BATCH_SIZE = 10; // text-embedding-v4模型最多支持10条
        private const int MAX_TOKEN_LENGTH = 8192; // text-embedding-v4每条最长支持8,192 Token
        private const int APPROX_CHARS_PER_TOKEN = 4; // 大约每4个字符=1个Token（估算值）

        public CodeSemanticSearch(
            string apiKey,
            string qdrantHost = "localhost",
            int qdrantPort = 6334,
            string collectionName = "code_embeddings")
        {
            _apiKey = apiKey;
            _qdrantHost = qdrantHost;
            _qdrantPort = qdrantPort;
            _collectionName = collectionName;
            
            // 连接Qdrant客户端
            Console.WriteLine($"[DEBUG] 尝试连接Qdrant服务器: {_qdrantHost}:{_qdrantPort}");
            _client = new QdrantClient(_qdrantHost, _qdrantPort);
            
            // 确保集合存在
            Console.WriteLine("[DEBUG] 开始初始化集合...");
            EnsureCollection().Wait();
            Console.WriteLine("[DEBUG] 集合初始化完成");
        }

        private async Task EnsureCollection()
        {
            try
            {
                Console.WriteLine($"[DEBUG] 检查集合是否存在: {_collectionName}");
                await _client.GetCollectionInfoAsync(_collectionName);
                Console.WriteLine($"[DEBUG] 集合 {_collectionName} 已存在");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] 集合不存在或连接失败: {ex.Message}");
                Console.WriteLine($"[DEBUG] 异常类型: {ex.GetType().Name}");
                
                // 创建新集合
                Console.WriteLine($"[DEBUG] 尝试创建新集合: {_collectionName}");
                await _client.CreateCollectionAsync(
                    _collectionName,
                    new VectorParams
                    {
                        Size = (ulong)_embeddingDim,
                        Distance = Distance.Cosine
                    });
                Console.WriteLine($"[DEBUG] 集合 {_collectionName} 创建成功");
            }
        }

        /// <summary>
        /// 估算文本的Token数量
        /// 这是一个简化的估算方法，实际Token数量可能会有差异
        /// </summary>
        private int EstimateTokenCount(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
                
            // 简化的Token估算规则
            // 1. 英文单词通常1个单词=1个Token
            // 2. 中文字符通常1个字符=1个Token
            // 3. 代码中的符号和关键字需要特殊处理
            
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
                    // 如果加上这一行会超过限制，就停止
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


        /// <summary>
        /// 提取代码片段 - 使用多语言解析器框架
        /// </summary>
        public List<CodeSnippet> ExtractCodeSnippets(string filePath)
        {
            try
            {
                Console.WriteLine($"[DEBUG] 开始解析文件: {filePath}");
                
                // 使用新的解析器工厂
                var parser = CodeParserFactory.GetParser(filePath);
                if (parser == null)
                {
                    Console.WriteLine($"[WARNING] 不支持的文件类型: {filePath}");
                    return new List<CodeSnippet>();
                }
                
                var snippets = parser.ParseCodeFile(filePath);
                
                Console.WriteLine($"[DEBUG] 文件 {filePath} 解析完成，共提取 {snippets.Count} 个代码片段");
                return snippets;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] 解析文件失败: {filePath}, 错误: {ex.Message}");
                Console.WriteLine($"[ERROR] 堆栈跟踪: {ex.StackTrace}");
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

        public async Task<int> ProcessCodebase(string codebasePath, List<string>? filePatterns = null)
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
                    await _client.UpsertAsync(_collectionName, points);
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

        public async Task<List<SearchResult>> Search(string query, int limit = 5)
        {
            // 生成查询的嵌入向量
            var queryEmbedding = await GetEmbeddings(new List<string> { query });
            
            // 执行搜索
            var queryVectorData = queryEmbedding[0].ToArray();
            
            var searchResult = await _client.SearchAsync(
                _collectionName,
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
    }
}