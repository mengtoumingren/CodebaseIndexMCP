using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CodebaseMcpServer.Services.Embedding.Models;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace CodebaseMcpServer.Services.Embedding.Providers
{
    public class DashScopeEmbeddingProvider : IEmbeddingProvider
    {
        private readonly EmbeddingProviderSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DashScopeEmbeddingProvider> _logger;
        
        // 常量定义
        private const int APPROX_CHARS_PER_TOKEN = 4; // 大约每4个字符为1个Token
        private const int MAX_BATCH_SIZE = 25; // DashScope 批量处理最大数量
        private const int MAX_TOKEN_LENGTH = 2048; // DashScope 最大Token长度
        private const int EMBEDDING_DIMENSION = 1024; // 嵌入向量维度

        public string ProviderName => "DashScope";

        public DashScopeEmbeddingProvider(
            EmbeddingProviderSettings settings,
            IHttpClientFactory httpClientFactory,
            ILogger<DashScopeEmbeddingProvider> logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (!ValidateConfiguration())
            {
                throw new InvalidOperationException($"Invalid configuration for {ProviderName} embedding provider.");
            }
        }

        public bool ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                _logger.LogError("{ProviderName}: ApiKey is missing.", ProviderName);
                return false;
            }
            if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                _logger.LogError("{ProviderName}: BaseUrl is missing.", ProviderName);
                return false;
            }
            if (string.IsNullOrWhiteSpace(_settings.Model))
            {
                _logger.LogError("{ProviderName}: Model is missing.", ProviderName);
                return false;
            }
            if (_settings.MaxBatchSize <= 0)
            {
                 _logger.LogError("{ProviderName}: MaxBatchSize must be greater than 0.", ProviderName);
                return false;
            }
            if (_settings.MaxTokenLength <= 0)
            {
                 _logger.LogError("{ProviderName}: MaxTokenLength must be greater than 0.", ProviderName);
                return false;
            }
            if (_settings.EmbeddingDimension <= 0)
            {
                 _logger.LogError("{ProviderName}: EmbeddingDimension must be greater than 0.", ProviderName);
                return false;
            }
            return true;
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

       public async Task<List<List<float>>> GetEmbeddingsAsync(List<string> texts)
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
                // var estimatedTokens = EstimateTokenCount(text);

                // if (estimatedTokens > MAX_TOKEN_LENGTH)
                // {
                //     Console.WriteLine($"[WARNING] 文本 {i} 长度过长 (约{estimatedTokens}个Token)，将被智能截断至 {MAX_TOKEN_LENGTH} Token");
                //     text = TruncateText(text, MAX_TOKEN_LENGTH);
                // }
                // else
                // {
                //     Console.WriteLine($"[DEBUG] 文本 {i} 长度: 约{estimatedTokens}个Token，符合限制");
                // }
                processedTexts.Add(text);
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_settings.ApiKey}");
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
                // BaseUrl validated non-null by ValidateConfiguration
                string baseUrl = _settings.BaseUrl!;
                string requestUrl = baseUrl.TrimEnd('/') + "/embeddings";
                var response = await httpClient.PostAsync(
                    requestUrl,
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
                    Enumerable.Repeat(0.0f, _settings.EmbeddingDimension).ToList(),
                    processedTexts.Count).ToList();
            }
        }

        public int GetMaxBatchSize() => _settings.MaxBatchSize;
        public int GetMaxTokenLength() => _settings.MaxTokenLength;
        public int GetEmbeddingDimension() => _settings.EmbeddingDimension;
    }
}