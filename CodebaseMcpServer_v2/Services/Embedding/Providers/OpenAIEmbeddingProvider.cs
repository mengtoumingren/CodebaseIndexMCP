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
    public class OpenAIEmbeddingProvider : IEmbeddingProvider
    {
        private readonly EmbeddingProviderSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OpenAIEmbeddingProvider> _logger;

        public string ProviderName => "OpenAI";

        public OpenAIEmbeddingProvider(
            EmbeddingProviderSettings settings,
            IHttpClientFactory httpClientFactory,
            ILogger<OpenAIEmbeddingProvider> logger)
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
            if (string.IsNullOrWhiteSpace(_settings.BaseUrl)) // BaseUrl for OpenAI is typically "https://api.openai.com/v1"
            {
                _logger.LogError("{ProviderName}: BaseUrl is missing.", ProviderName);
                return false;
            }
            if (string.IsNullOrWhiteSpace(_settings.Model))
            {
                _logger.LogError("{ProviderName}: Model is missing.", ProviderName);
                return false;
            }
            // Other checks like MaxBatchSize, MaxTokenLength, EmbeddingDimension are inherited
            return true;
        }

        public async Task<List<List<float>>> GetEmbeddingsAsync(List<string> texts)
        {
            if (texts == null || texts.Count == 0)
            {
                _logger.LogWarning("{ProviderName}: Input text list is null or empty.", ProviderName);
                return new List<List<float>>();
            }

            if (texts.Count > _settings.MaxBatchSize) // OpenAI API might have its own batch limits, ensure config reflects this.
            {
                 _logger.LogError("{ProviderName}: Batch size {ActualSize} exceeds configured maximum {MaxBatchSize}.",
                    ProviderName, texts.Count, _settings.MaxBatchSize);
                throw new ArgumentException($"Batch size cannot exceed configured {_settings.MaxBatchSize}. Current size: {texts.Count}", nameof(texts));
            }

            _logger.LogDebug("{ProviderName}: Requesting embeddings for {Count} texts using model {Model}.", ProviderName, texts.Count, _settings.Model);

            var httpClient = _httpClientFactory.CreateClient(ProviderName);
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey ?? string.Empty);
            
            var requestPayloadBase = new
            {
                input = texts,
                model = _settings.Model // Model validated non-null
            };
            
            object actualPayload;
            // Model validated non-null by ValidateConfiguration
            string modelName = _settings.Model!;
            int defaultDimForModel = GetEmbeddingDimensionFromModelName(modelName);

            if ((modelName.Contains("text-embedding-3") || modelName.Contains("ada-002")) &&
                _settings.EmbeddingDimension > 0 &&
                _settings.EmbeddingDimension != defaultDimForModel)
            {
                 actualPayload = new
                {
                    requestPayloadBase.input,
                    requestPayloadBase.model,
                    dimensions = _settings.EmbeddingDimension
                };
                _logger.LogInformation("{ProviderName}: Using custom dimension {Dimension} for model {Model}.", ProviderName, _settings.EmbeddingDimension, modelName);
            }
            else
            {
                actualPayload = requestPayloadBase;
            }

            // BaseUrl validated non-null by ValidateConfiguration
            string baseUrl = _settings.BaseUrl!;
            string requestUrl = baseUrl.TrimEnd('/') + "/embeddings";

            try
            {
                // Removed JsonSerializerOptions from PostAsJsonAsync, relying on default behavior for anonymous types
                // or OpenAI API's tolerance for null fields if 'dimensions' is not applicable.
                var response = await httpClient.PostAsJsonAsync(requestUrl, actualPayload);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var embeddingResponse = JsonSerializer.Deserialize<EmbeddingResponse>(responseContent); // Uses our generic response model
                    var resultEmbeddings = new List<List<float>>();
                    if (embeddingResponse?.Data != null)
                    {
                        foreach (var data in embeddingResponse.Data)
                        {
                            resultEmbeddings.Add(data.Embedding);
                        }
                    }
                    _logger.LogInformation("{ProviderName}: Successfully retrieved {Count} embeddings.", ProviderName, resultEmbeddings.Count);
                    return resultEmbeddings;
                }
                else
                {
                    _logger.LogError("Error fetching embeddings from {ProviderName}. Status: {StatusCode}. Response: {ErrorContent}",
                        ProviderName, response.StatusCode, responseContent);
                    // Attempt to parse OpenAI error object
                    try
                    {
                        var errorObj = JsonDocument.Parse(responseContent).RootElement;
                        if (errorObj.TryGetProperty("error", out var errorDetails))
                        {
                            string errorMessage = errorDetails.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String ? msg.GetString()! : responseContent;
                            string errorType = errorDetails.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String ? type.GetString()! : "N/A";
                            throw new HttpRequestException($"Error from {ProviderName}: {errorMessage} (Type: {errorType})");
                        }
                    }
                    catch (JsonException) { /* Ignore if parsing error object fails */ }
                    // Fallback exception if custom error parsing fails or no "error" property
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
        
        private int GetEmbeddingDimensionFromModelName(string? modelName) // Made modelName nullable
        {
            if (string.IsNullOrEmpty(modelName))
            {
                return _settings.EmbeddingDimension > 0 ? _settings.EmbeddingDimension : 1536; // Default fallback if model name is missing
            }
            // Default dimensions for known OpenAI models
            if (modelName.Contains("text-embedding-3-large")) return 3072;
            if (modelName.Contains("text-embedding-3-small")) return 1536;
            if (modelName.Contains("ada-002")) return 1536; // text-embedding-ada-002
            return _settings.EmbeddingDimension > 0 ? _settings.EmbeddingDimension : 1536; // Fallback to configured or a common default
        }


        public int GetMaxBatchSize() => _settings.MaxBatchSize > 0 ? _settings.MaxBatchSize : 2048;
        public int GetMaxTokenLength() => _settings.MaxTokenLength > 0 ? _settings.MaxTokenLength : 8191;
        public int GetEmbeddingDimension() => GetEmbeddingDimensionFromModelName(_settings.Model); // Model can be null here if not configured
    }
}