using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CodebaseMcpServer.Services.Embedding.Models;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Services.Embedding.Providers
{
    public class OllamaEmbeddingProvider : IEmbeddingProvider
    {
        private readonly EmbeddingProviderSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OllamaEmbeddingProvider> _logger;

        public string ProviderName => "Ollama";

        public OllamaEmbeddingProvider(
            EmbeddingProviderSettings settings,
            IHttpClientFactory httpClientFactory,
            ILogger<OllamaEmbeddingProvider> logger)
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
            
            if (_settings.EmbeddingDimension <= 0)
            {
                _logger.LogError("{ProviderName}: EmbeddingDimension must be greater than 0.", ProviderName);
                return false;
            }
            
            return true;
        }

        public async Task<List<List<float>>> GetEmbeddingsAsync(List<string> texts)
        {
            if (texts == null || texts.Count == 0)
            {
                _logger.LogWarning("{ProviderName}: Input text list is null or empty.", ProviderName);
                return new List<List<float>>();
            }

            if (texts.Count > _settings.MaxBatchSize)
            {
                _logger.LogError("{ProviderName}: Batch size {ActualSize} exceeds configured maximum {MaxBatchSize}.",
                    ProviderName, texts.Count, _settings.MaxBatchSize);
                throw new ArgumentException($"Batch size cannot exceed configured {_settings.MaxBatchSize}. Current size: {texts.Count}", nameof(texts));
            }

            _logger.LogDebug("{ProviderName}: Requesting embeddings for {Count} texts using model {Model}.", 
                ProviderName, texts.Count, _settings.Model);

            var httpClient = _httpClientFactory.CreateClient(ProviderName);
            httpClient.Timeout = TimeSpan.FromMilliseconds(_settings.Timeout);

            var embeddings = new List<List<float>>();

            try
            {
                // Ollama通常需要逐个处理文本，或者支持批量处理（取决于具体实现）
                foreach (var text in texts)
                {
                    var embedding = await GetSingleEmbeddingAsync(httpClient, text);
                    embeddings.Add(embedding);
                }

                _logger.LogInformation("{ProviderName}: Successfully retrieved {Count} embeddings.", 
                    ProviderName, embeddings.Count);
                return embeddings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ProviderName}: Exception occurred while fetching embeddings.", ProviderName);
                throw;
            }
        }

        private async Task<List<float>> GetSingleEmbeddingAsync(HttpClient httpClient, string text)
        {
            var requestPayload = new
            {
                model = _settings.Model,
                prompt = text
            };

            string baseUrl = _settings.BaseUrl!;
            string requestUrl = baseUrl.TrimEnd('/') + "/api/embeddings";

            var response = await httpClient.PostAsJsonAsync(requestUrl, requestPayload);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var jsonDocument = JsonDocument.Parse(responseContent);
                if (jsonDocument.RootElement.TryGetProperty("embedding", out var embeddingElement))
                {
                    var embedding = new List<float>();
                    foreach (var element in embeddingElement.EnumerateArray())
                    {
                        embedding.Add(element.GetSingle());
                    }
                    return embedding;
                }
                else
                {
                    _logger.LogError("{ProviderName}: Response does not contain 'embedding' property. Response: {Response}", 
                        ProviderName, responseContent);
                    throw new InvalidOperationException($"Invalid response format from {ProviderName}");
                }
            }
            else
            {
                _logger.LogError("Error fetching embedding from {ProviderName}. Status: {StatusCode}. Response: {ErrorContent}",
                    ProviderName, response.StatusCode, responseContent);
                throw new HttpRequestException($"Error fetching embedding from {ProviderName}: {response.StatusCode} - {responseContent}");
            }
        }

        public int GetMaxBatchSize() => _settings.MaxBatchSize > 0 ? _settings.MaxBatchSize : 100;
        public int GetMaxTokenLength() => _settings.MaxTokenLength > 0 ? _settings.MaxTokenLength : 8192;
        public int GetEmbeddingDimension() => _settings.EmbeddingDimension > 0 ? _settings.EmbeddingDimension : 768;
    }
}