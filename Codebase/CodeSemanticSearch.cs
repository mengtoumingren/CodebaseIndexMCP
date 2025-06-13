using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Qdrant.Client;
using Qdrant.Client.Grpc;

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

    // DashScope API响应模型
    public class EmbeddingResponse
    {
        public int StatusCode { get; set; }
        public string RequestId { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public EmbeddingOutput Output { get; set; } = new();
    }

    public class EmbeddingOutput
    {
        public List<EmbeddingItem> Embeddings { get; set; } = new();
    }

    public class EmbeddingItem
    {
        public List<float> Embedding { get; set; } = new();
        public int TextIndex { get; set; }
    }

    // 代码语义搜索主类
    public class CodeSemanticSearch
    {
        private readonly string _apiKey;
        private readonly string _qdrantHost;
        private readonly int _qdrantPort;
        private readonly string _collectionName;
        private readonly int _embeddingDim = 1024;
        private readonly QdrantClient _client;

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

        private async Task<List<List<float>>> GetEmbeddings(List<string> texts)
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            
            var payload = new
            {
                model = "text-embedding-v4",
                input = texts,
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
                
                return embeddingResponse?.Output?.Embeddings
                    ?.Select(item => item.Embedding)
                    ?.ToList() ?? new List<List<float>>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取嵌入向量失败: {ex.Message}");
                // 返回零向量作为备选
                return Enumerable.Repeat(
                    Enumerable.Repeat(0.0f, _embeddingDim).ToList(), 
                    texts.Count).ToList();
            }
        }

        public List<CodeSnippet> ExtractCSharpSnippets(string filePath)
        {
            var snippets = new List<CodeSnippet>();
            
            try
            {
                var content = File.ReadAllText(filePath);
                var lines = content.Split('\n');
                
                // C#类和方法的正则表达式模式
                var classPattern = new Regex(@"\s*(public|private|protected|internal|sealed|abstract|static)?\s*class\s+(\w+)\s*[:{]");
                var methodPattern = new Regex(@"\s*(public|private|protected|internal|static|virtual|override|abstract)?\s*([\w<>]+)\s+(\w+)\s*\([^)]*\)\s*{");
                
                string? currentNamespace = null;
                string? currentClass = null;
                
                for (int i = 0; i < lines.Length; i++)
                {
                    // 检测命名空间
                    var namespaceMatch = Regex.Match(lines[i], @"\s*namespace\s+(\w+(?:\.\w+)*)\s*{");
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
                        currentClass = classMatch.Groups[2].Value;
                        continue;
                    }
                    
                    // 检测方法定义
                    var methodMatch = methodPattern.Match(lines[i]);
                    if (methodMatch.Success && currentClass != null)
                    {
                        var methodName = methodMatch.Groups[3].Value;
                        
                        // 提取方法体
                        var methodBody = new List<string>();
                        int j = i + 1;
                        int braceCount = 1; // 当前方法的左大括号计数
                        
                        while (j < lines.Length)
                        {
                            braceCount += lines[j].Count(c => c == '{') - lines[j].Count(c => c == '}');
                            methodBody.Add(lines[j]);
                            
                            if (braceCount == 0)
                                break;
                                
                            j++;
                        }
                        
                        var methodContent = string.Join("\n", methodBody);
                        
                        // 创建代码片段信息
                        var snippet = new CodeSnippet
                        {
                            FilePath = filePath,
                            Namespace = currentNamespace,
                            ClassName = currentClass,
                            MethodName = methodName,
                            Code = lines[i] + "\n" + methodContent,
                            StartLine = i + 1,
                            EndLine = j + 1
                        };
                        
                        snippets.Add(snippet);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理文件 {filePath} 失败: {ex.Message}");
            }
            
            return snippets;
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

        private async Task BatchIndexSnippets(List<CodeSnippet> snippets, int batchSize = 64)
        {
            for (int i = 0; i < snippets.Count; i += batchSize)
            {
                var batch = snippets.Skip(i).Take(batchSize).ToList();
                
                // 提取代码文本
                var codes = batch.Select(snippet => snippet.Code).ToList();
                
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
            }
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