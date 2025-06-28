using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodebaseMcpServer.Infrastructure.Embedding.Models
{
    /// <summary>
    /// Represents the overall configuration for embedding providers, typically loaded from appsettings.json.
    /// </summary>
    public class EmbeddingConfiguration
    {
        public const string ConfigSectionName = "EmbeddingProviders";

        /// <summary>
        /// The name of the default embedding provider to use if not specified elsewhere.
        /// This should match one of the keys in the Providers dictionary.
        /// </summary>
        [JsonPropertyName("defaultProvider")]
        public string? DefaultProvider { get; set; }

        /// <summary>
        /// A dictionary containing configurations for all available embedding providers.
        /// The key is the provider name (e.g., "DashScope", "OpenAI"),
        /// and the value is an EmbeddingProviderSettings object.
        /// </summary>
        [JsonPropertyName("providers")]
        public Dictionary<string, EmbeddingProviderSettings> Providers { get; set; } = new Dictionary<string, EmbeddingProviderSettings>();
    }
}
