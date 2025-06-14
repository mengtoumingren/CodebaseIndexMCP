using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CodebaseMcpServer.Services.Embedding.Models;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Services.Embedding.Providers
{
    public class DashScopeEmbeddingProvider : IEmbeddingProvider
    {
        private readonly EmbeddingProviderSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<DashScopeEmbeddingProvider> _logger;

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

        public async Task<List<List<float>>> GetEmbeddingsAsync(List<string> texts)
        {
            if (texts == null || texts.Count == 0)
            {
                _logger.LogWarning("{ProviderName}: Input text list is null or empty.", ProviderName);
                return new List<List<float>>();
            }

            if (texts.Count > _settings.MaxBatchSize)
            {
                _logger.LogError("{ProviderName}: Batch size {ActualSize} exceeds maximum allowed {MaxBatchSize}.",
                    ProviderName, texts.Count, _settings.MaxBatchSize);
                throw new ArgumentException($"Batch size cannot exceed {_settings.MaxBatchSize}. Current size: {texts.Count}", nameof(texts));
            }
            
            _logger.LogDebug("{ProviderName}: Requesting embeddings for {Count} texts.", ProviderName, texts.Count);

            var httpClient = _httpClientFactory.CreateClient(ProviderName);
            // Ensure ApiKey is not null before using it, though ValidateConfiguration should have caught this.
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey ?? string.Empty);
            httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            
            var payload = new
            {
                model = _settings.Model, // Model should be validated as non-null by ValidateConfiguration
                input = new { texts = texts },
                parameters = new { output_dimension = _settings.EmbeddingDimension }
            };
            
            var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
            
            // BaseUrl should be validated as non-null by ValidateConfiguration
            string baseUrl = _settings.BaseUrl!;
            string requestUrl = baseUrl;
            if (!baseUrl.EndsWith("/embeddings"))
            {
                requestUrl = baseUrl.TrimEnd('/') + "/embeddings";
            }

            try
            {
                _logger.LogDebug("{ProviderName}: Sending request to {Url} with model {Model}", ProviderName, requestUrl, _settings.Model ?? "N/A");
                var response = await httpClient.PostAsync(requestUrl, content);
                
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // DashScope's response structure is slightly different from the generic EmbeddingResponse
                    // It has "output.embeddings" which is a list of objects, each with an "embedding" array.
                    JsonDocument parsedResponse = JsonDocument.Parse(responseContent);
                    if (parsedResponse.RootElement.TryGetProperty("output", out var outputElement) &&
                        outputElement.TryGetProperty("embeddings", out var embeddingsElement))
                    {
                        var resultEmbeddings = new List<List<float>>();
                        foreach (var embeddingItem in embeddingsElement.EnumerateArray())
                        {
                            if (embeddingItem.TryGetProperty("embedding", out var vectorElement))
                            {
                                var vector = JsonSerializer.Deserialize<List<float>>(vectorElement.GetRawText());
                                if (vector != null)
                                {
                                    resultEmbeddings.Add(vector);
                                }
                            }
                        }
                        _logger.LogInformation("{ProviderName}: Successfully retrieved {Count} embeddings.", ProviderName, resultEmbeddings.Count);
                        return resultEmbeddings;
                    }
                    else
                    {
                        _logger.LogError("{ProviderName}: Unexpected response structure. 'output.embeddings' not found. Response: {Response}", ProviderName, responseContent);
                        throw new HttpRequestException($"Error fetching embeddings from {ProviderName}: Unexpected response structure.");
                    }
                }
                else
                {
                    _logger.LogError("Error fetching embeddings from {ProviderName}. Status: {StatusCode}. Response: {ErrorContent}",
                        ProviderName, response.StatusCode, responseContent);
                    throw new HttpRequestException($"Error fetching embeddings from {ProviderName}: {response.StatusCode} - {responseContent}");
                }
            }
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "{ProviderName}: Failed to parse JSON response.", ProviderName);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ProviderName}: Exception occurred while fetching embeddings.", ProviderName);
                throw;
            }
        }

        public int GetMaxBatchSize() => _settings.MaxBatchSize;
        public int GetMaxTokenLength() => _settings.MaxTokenLength;
        public int GetEmbeddingDimension() => _settings.EmbeddingDimension;
    }
}