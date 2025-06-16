using System;
using System.Collections.Generic;
using CodebaseMcpServer.Models;
using CodebaseMcpServer.Services.Embedding.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodebaseMcpServer.Services.Embedding
{
    /// <summary>
    /// Factory for creating instances of IEmbeddingProvider based on configuration.
    /// </summary>
    public class EmbeddingProviderFactory
    {
        private readonly EmbeddingConfiguration _embeddingConfiguration;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<EmbeddingProviderFactory> _logger;

        public EmbeddingProviderFactory(
            IOptions<EmbeddingConfiguration> embeddingConfigurationOptions,
            IServiceProvider serviceProvider,
            ILogger<EmbeddingProviderFactory> logger)
        {
            _embeddingConfiguration = embeddingConfigurationOptions.Value ?? throw new ArgumentNullException(nameof(embeddingConfigurationOptions), "EmbeddingConfiguration cannot be null.");
            _serviceProvider = serviceProvider;
            _logger = logger;

            if (_embeddingConfiguration.Providers == null || _embeddingConfiguration.Providers.Count == 0)
            {
                _logger.LogWarning("No embedding providers configured in EmbeddingProviders:Providers section.");
            }
        }

        /// <summary>
        /// Gets the configured default embedding provider.
        /// </summary>
        /// <returns>An instance of the default IEmbeddingProvider.</returns>
        /// <exception cref="InvalidOperationException">If the default provider is not configured or not found.</exception>
        public IEmbeddingProvider GetDefaultProvider()
        {
            if (string.IsNullOrWhiteSpace(_embeddingConfiguration.DefaultProvider))
            {
                _logger.LogError("Default embedding provider is not specified in configuration.");
                throw new InvalidOperationException("Default embedding provider is not specified in configuration.");
            }

            if (!Enum.TryParse<EmbeddingProviderType>(_embeddingConfiguration.DefaultProvider, true, out var providerType))
            {
                _logger.LogError($"Configured default provider name '{_embeddingConfiguration.DefaultProvider}' is not a valid EmbeddingProviderType.");
                throw new InvalidOperationException($"Configured default provider name '{_embeddingConfiguration.DefaultProvider}' is not a valid EmbeddingProviderType.");
            }
            
            return GetProvider(providerType);
        }

        /// <summary>
        /// Gets a specific embedding provider by type.
        /// </summary>
        /// <param name="providerType">The type of the embedding provider to get.</param>
        /// <returns>An instance of the specified IEmbeddingProvider.</returns>
        /// <exception cref="NotSupportedException">If the provider type is not supported or configured.</exception>
        /// <exception cref="InvalidOperationException">If the provider configuration is missing or invalid.</exception>
        public IEmbeddingProvider GetProvider(EmbeddingProviderType providerType)
        {
            var providerName = providerType.ToString();
            if (!_embeddingConfiguration.Providers.TryGetValue(providerName, out var providerSettings))
            {
                _logger.LogError($"Configuration for provider '{providerName}' not found.");
                throw new InvalidOperationException($"Configuration for provider '{providerName}' not found.");
            }

            if (providerSettings == null)
            {
                 _logger.LogError($"Settings for provider '{providerName}' are null.");
                throw new InvalidOperationException($"Settings for provider '{providerName}' are null.");
            }

            switch (providerType)
            {
                case EmbeddingProviderType.DashScope:
                    var httpClientFactory = _serviceProvider.GetService<IHttpClientFactory>();
                    var dashScopeLogger = _serviceProvider.GetService<ILogger<Providers.DashScopeEmbeddingProvider>>();
                    if (httpClientFactory == null)
                    {
                        _logger.LogError("IHttpClientFactory not resolved from service provider. Cannot create DashScopeEmbeddingProvider.");
                        throw new InvalidOperationException("IHttpClientFactory not available for DashScopeEmbeddingProvider.");
                    }
                    if (dashScopeLogger == null)
                    {
                         _logger.LogWarning("ILogger<DashScopeEmbeddingProvider> not resolved. DashScope provider will run without specific logging.");
                        // Optionally, create a null logger or use the factory's logger. For now, let's proceed but this indicates a potential setup issue.
                        // dashScopeLogger = _logger; // Not ideal type match.
                    }
                    return new Providers.DashScopeEmbeddingProvider(providerSettings, httpClientFactory, dashScopeLogger!); // Null forgiving if logger is optional

                case EmbeddingProviderType.OpenAI:
                    var openAiHttpClientFactory = _serviceProvider.GetService<IHttpClientFactory>();
                    var openAiLogger = _serviceProvider.GetService<ILogger<Providers.OpenAIEmbeddingProvider>>();
                    if (openAiHttpClientFactory == null)
                    {
                        _logger.LogError("IHttpClientFactory not resolved from service provider. Cannot create OpenAIEmbeddingProvider.");
                        throw new InvalidOperationException("IHttpClientFactory not available for OpenAIEmbeddingProvider.");
                    }
                     // Logger can be optional, OpenAIEmbeddingProvider handles null logger.
                    return new Providers.OpenAIEmbeddingProvider(providerSettings, openAiHttpClientFactory, openAiLogger!);

                case EmbeddingProviderType.AzureOpenAI:
                    var azureHttpClientFactory = _serviceProvider.GetService<IHttpClientFactory>();
                    var azureLogger = _serviceProvider.GetService<ILogger<Providers.AzureOpenAIEmbeddingProvider>>();
                    if (azureHttpClientFactory == null)
                    {
                        _logger.LogError("IHttpClientFactory not resolved from service provider. Cannot create AzureOpenAIEmbeddingProvider.");
                        throw new InvalidOperationException("IHttpClientFactory not available for AzureOpenAIEmbeddingProvider.");
                    }
                    return new Providers.AzureOpenAIEmbeddingProvider(providerSettings, azureHttpClientFactory, azureLogger!);

                case EmbeddingProviderType.HuggingFace:
                    var hfHttpClientFactory = _serviceProvider.GetService<IHttpClientFactory>();
                    var hfLogger = _serviceProvider.GetService<ILogger<Providers.HuggingFaceEmbeddingProvider>>();
                    if (hfHttpClientFactory == null)
                    {
                        _logger.LogError("IHttpClientFactory not resolved from service provider. Cannot create HuggingFaceEmbeddingProvider.");
                        throw new InvalidOperationException("IHttpClientFactory not available for HuggingFaceEmbeddingProvider.");
                    }
                    return new Providers.HuggingFaceEmbeddingProvider(providerSettings, hfHttpClientFactory, hfLogger!);

                case EmbeddingProviderType.Ollama:
                    var ollamaHttpClientFactory = _serviceProvider.GetService<IHttpClientFactory>();
                    var ollamaLogger = _serviceProvider.GetService<ILogger<Providers.OllamaEmbeddingProvider>>();
                    if (ollamaHttpClientFactory == null)
                    {
                        _logger.LogError("IHttpClientFactory not resolved from service provider. Cannot create OllamaEmbeddingProvider.");
                        throw new InvalidOperationException("IHttpClientFactory not available for OllamaEmbeddingProvider.");
                    }
                    return new Providers.OllamaEmbeddingProvider(providerSettings, ollamaHttpClientFactory, ollamaLogger!);
                    
                // Add cases for other providers here as they are implemented.

                default:
                    _logger.LogWarning($"Provider type '{providerType}' is recognized but no concrete implementation is registered in the factory.");
                    throw new NotSupportedException($"Provider type '{providerType}' is not yet implemented in EmbeddingProviderFactory.");
            }
        }
         /// <summary>
        /// Gets all configured provider settings.
        /// </summary>
        public IReadOnlyDictionary<string, EmbeddingProviderSettings> GetAllProviderSettings()
        {
            return _embeddingConfiguration.Providers;
        }
    }
}