using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CodebaseMcpServer.Services.Embedding.Models;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Services.Embedding.Providers
{
    public class HuggingFaceEmbeddingProvider : IEmbeddingProvider
    {
        private readonly EmbeddingProviderSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<HuggingFaceEmbeddingProvider> _logger;

        public string ProviderName => "HuggingFace";

        // HuggingFace specific response structure for feature-extraction (embeddings)
        private class HuggingFaceEmbeddingApiResponse : List<List<float>> { }


        public HuggingFaceEmbeddingProvider(
            EmbeddingProviderSettings settings,
            IHttpClientFactory httpClientFactory,
            ILogger<HuggingFaceEmbeddingProvider> logger)
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
            // API Key for HuggingFace is optional for some public models, but usually required for private/rate-limited ones.
            // if (string.IsNullOrWhiteSpace(_settings.ApiKey))
            // {
            //     _logger.LogWarning("{ProviderName}: ApiKey is not set. This might be an issue for private models or if rate limits are hit.", ProviderName);
            // }
            if (string.IsNullOrWhiteSpace(_settings.BaseUrl)) // e.g., https://api-inference.huggingface.co
            {
                _logger.LogError("{ProviderName}: BaseUrl is missing.", ProviderName);
                return false;
            }
            if (string.IsNullOrWhiteSpace(_settings.Model)) // e.g., sentence-transformers/all-MiniLM-L6-v2
            {
                _logger.LogError("{ProviderName}: Model (model ID) is missing.", ProviderName);
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

            // HuggingFace Inference API for sentence-embeddings typically processes one input at a time,
            // or a batch if the specific model/task supports it.
            // The standard "feature-extraction" task often takes a single "inputs" string or an array.
            // Let's assume the configured MaxBatchSize is respected if the model supports batching.
            if (texts.Count > _settings.MaxBatchSize)
            {
                 _logger.LogError("{ProviderName}: Batch size {ActualSize} exceeds configured maximum {MaxBatchSize}.",
                    ProviderName, texts.Count, _settings.MaxBatchSize);
                throw new ArgumentException($"Batch size cannot exceed configured {_settings.MaxBatchSize}. Current size: {texts.Count}", nameof(texts));
            }

            _logger.LogDebug("{ProviderName}: Requesting embeddings for {Count} texts using model {Model}.", ProviderName, texts.Count, _settings.Model);

            var httpClient = _httpClientFactory.CreateClient(ProviderName); // Assumes named HttpClient "HuggingFace"
            if (!string.IsNullOrWhiteSpace(_settings.ApiKey))
            {
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ApiKey);
            }
            
            // Payload for HuggingFace feature-extraction (embeddings)
            // It expects an "inputs" field which can be a string or array of strings.
            // And an optional "options" field.
            var requestPayload = new
            {
                inputs = texts.Count == 1 ? (object)texts[0] : texts, // API might prefer single string if only one input
                options = new { wait_for_model = true } // Optional: wait if model is loading
            };
            
            // URL: {BaseUrl}/pipeline/feature-extraction/{ModelId} OR /models/{ModelId}
            // Using /models/{ModelId} is more common for direct model inference.
            // BaseUrl and Model validated non-null by ValidateConfiguration
            string baseUrl = _settings.BaseUrl!;
            string modelId = _settings.Model!;
            string requestUrl = $"{baseUrl.TrimEnd('/')}/pipeline/feature-extraction/{modelId}";
            // Alternative if using direct model endpoint:
            // string requestUrl = $"{baseUrl.TrimEnd('/')}/models/{modelId}";


            try
            {
                var response = await httpClient.PostAsJsonAsync(requestUrl, requestPayload);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    // HuggingFace response for feature-extraction is typically a list of vectors (List<List<float>>)
                    // or a single vector if input was a single string.
                    // If input was a list, output is List<List<float>>. If input was single string, output is List<float>.
                    
                    List<List<float>>? resultEmbeddings = null;

                    if (texts.Count == 1)
                    {
                        var singleEmbedding = JsonSerializer.Deserialize<List<float>>(responseContent);
                        if (singleEmbedding != null)
                        {
                            resultEmbeddings = new List<List<float>> { singleEmbedding };
                        }
                    }
                    else
                    {
                         resultEmbeddings = JsonSerializer.Deserialize<HuggingFaceEmbeddingApiResponse>(responseContent);
                    }
                    
                    if (resultEmbeddings != null)
                    {
                        _logger.LogInformation("{ProviderName}: Successfully retrieved {Count} embeddings.", ProviderName, resultEmbeddings.Count);
                        return resultEmbeddings;
                    }
                    else
                    {
                        _logger.LogError("{ProviderName}: Failed to deserialize response or response was empty. Response: {Response}", ProviderName, responseContent);
                        throw new HttpRequestException($"Error fetching embeddings from {ProviderName}: Failed to deserialize or empty response.");
                    }
                }
                else
                {
                    _logger.LogError("Error fetching embeddings from {ProviderName}. Status: {StatusCode}. URL: {RequestUrl}. Response: {ErrorContent}",
                        ProviderName, response.StatusCode, requestUrl, responseContent);
                    // Attempt to parse HuggingFace error object
                    try
                    {
                        var errorObj = JsonDocument.Parse(responseContent).RootElement;
                        if (errorObj.TryGetProperty("error", out var errorMsg) && errorMsg.ValueKind == JsonValueKind.String)
                        {
                            throw new HttpRequestException($"Error from {ProviderName}: {errorMsg.GetString()}");
                        }
                         if (errorObj.TryGetProperty("detail", out var detailMsg) && detailMsg.ValueKind == JsonValueKind.String) // Sometimes error is in "detail"
                        {
                            throw new HttpRequestException($"Error from {ProviderName}: {detailMsg.GetString()}");
                        }
                    }
                    catch (JsonException) { /* Ignore */ }
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

        public int GetMaxBatchSize() => _settings.MaxBatchSize > 0 ? _settings.MaxBatchSize : 1; // Default to 1 for safety, many HF models are single-input for this task unless specified.
        public int GetMaxTokenLength() => _settings.MaxTokenLength > 0 ? _settings.MaxTokenLength : 512; // Common for sentence-transformers
        public int GetEmbeddingDimension() => _settings.EmbeddingDimension; // Must be configured correctly for the chosen model
    }
}