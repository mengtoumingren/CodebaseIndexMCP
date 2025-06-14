using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web; // Required for HttpUtility
using CodebaseMcpServer.Services.Embedding.Models;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Services.Embedding.Providers
{
    public class AzureOpenAIEmbeddingProvider : IEmbeddingProvider
    {
        private readonly EmbeddingProviderSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AzureOpenAIEmbeddingProvider> _logger;

        public string ProviderName => "AzureOpenAI";

        public AzureOpenAIEmbeddingProvider(
            EmbeddingProviderSettings settings,
            IHttpClientFactory httpClientFactory,
            ILogger<AzureOpenAIEmbeddingProvider> logger)
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
            if (string.IsNullOrWhiteSpace(_settings.BaseUrl)) // e.g., https://your-resource.openai.azure.com
            {
                _logger.LogError("{ProviderName}: BaseUrl is missing.", ProviderName);
                return false;
            }
            if (string.IsNullOrWhiteSpace(_settings.DeploymentName)) // Deployment name for the embedding model
            {
                _logger.LogError("{ProviderName}: DeploymentName is missing.", ProviderName);
                return false;
            }
            if (string.IsNullOrWhiteSpace(_settings.ApiVersion)) // e.g., 2024-02-01
            {
                _logger.LogError("{ProviderName}: ApiVersion is missing.", ProviderName);
                return false;
            }
            if (string.IsNullOrWhiteSpace(_settings.Model)) // Model name is often part of the deployment, but good to have for reference
            {
                _logger.LogWarning("{ProviderName}: Model name is not explicitly configured, relying on deployment. This is usually fine for Azure OpenAI.", ProviderName);
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

            if (texts.Count > _settings.MaxBatchSize) // Azure OpenAI also has batch limits (e.g., 16 for ada-002)
            {
                _logger.LogError("{ProviderName}: Batch size {ActualSize} exceeds configured maximum {MaxBatchSize}.",
                    ProviderName, texts.Count, _settings.MaxBatchSize);
                throw new ArgumentException($"Batch size cannot exceed configured {_settings.MaxBatchSize}. Current size: {texts.Count}", nameof(texts));
            }

            _logger.LogDebug("{ProviderName}: Requesting embeddings for {Count} texts using deployment {Deployment}, model {Model}.", 
                ProviderName, texts.Count, _settings.DeploymentName, _settings.Model);

            var httpClient = _httpClientFactory.CreateClient(ProviderName);
            httpClient.DefaultRequestHeaders.Add("api-key", _settings.ApiKey ?? string.Empty); // ApiKey validated non-null
            
            var requestPayloadBase = new
            {
                input = texts,
            };

            object actualPayload;
            // Model can be null/empty for Azure if deployment implies model, but check for dimension logic
            string? modelName = _settings.Model;
            int defaultDimForModel = GetEmbeddingDimensionFromModelName(modelName);

            if (_settings.EmbeddingDimension > 0 && _settings.EmbeddingDimension != defaultDimForModel)
            {
                 actualPayload = new
                {
                    requestPayloadBase.input,
                    dimensions = _settings.EmbeddingDimension
                };
                _logger.LogInformation("{ProviderName}: Using custom dimension {Dimension} for deployment {Deployment}.",
                    ProviderName, _settings.EmbeddingDimension, _settings.DeploymentName ?? "N/A");
            }
            else
            {
                actualPayload = requestPayloadBase;
            }
            
            // BaseUrl, DeploymentName, ApiVersion validated non-null by ValidateConfiguration
            string baseUrl = _settings.BaseUrl!;
            string deploymentName = _settings.DeploymentName!;
            string apiVersion = _settings.ApiVersion!;

            var uriBuilder = new UriBuilder(baseUrl.TrimEnd('/'));
            uriBuilder.Path += $"/openai/deployments/{deploymentName}/embeddings";
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["api-version"] = apiVersion;
            uriBuilder.Query = query.ToString();
            string requestUrl = uriBuilder.ToString();

            try
            {
                var response = await httpClient.PostAsJsonAsync(requestUrl, actualPayload, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
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
                    _logger.LogError("Error fetching embeddings from {ProviderName}. Status: {StatusCode}. URL: {RequestUrl}. Response: {ErrorContent}",
                        ProviderName, response.StatusCode, requestUrl, responseContent);
                     try
                    {
                        var errorObj = JsonDocument.Parse(responseContent).RootElement;
                        if (errorObj.TryGetProperty("error", out var errorDetails))
                        {
                            string errorMessage = errorDetails.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String ? msg.GetString()! : responseContent;
                            string errorType = errorDetails.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String ? code.GetString()! : "N/A"; // Azure uses "code" for error type
                            throw new HttpRequestException($"Error from {ProviderName}: {errorMessage} (Code: {errorType})");
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
        
        private int GetEmbeddingDimensionFromModelName(string? modelName) // Made modelName nullable
        {
            if (string.IsNullOrEmpty(modelName))
            {
                // For Azure, the dimension is often tied to the deployment, not a model name in settings.
                // Fallback to configured dimension or a common default if not specified.
                return _settings.EmbeddingDimension > 0 ? _settings.EmbeddingDimension : 1536;
            }
            
            if (modelName.Contains("text-embedding-3-large")) return 3072;
            if (modelName.Contains("text-embedding-3-small")) return 1536;
            if (modelName.Contains("text-embedding-ada-002")) return 1536;
            return _settings.EmbeddingDimension > 0 ? _settings.EmbeddingDimension : 1536; // Fallback
        }

        public int GetMaxBatchSize() => _settings.MaxBatchSize > 0 ? _settings.MaxBatchSize : 16;
        public int GetMaxTokenLength() => _settings.MaxTokenLength > 0 ? _settings.MaxTokenLength : 8191;
        public int GetEmbeddingDimension() => GetEmbeddingDimensionFromModelName(_settings.Model); // Model can be null
    }
}