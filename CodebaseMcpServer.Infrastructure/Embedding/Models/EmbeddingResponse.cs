using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodebaseMcpServer.Infrastructure.Embedding.Models
{
    /// <summary>
    /// Represents a generic response from an embedding provider.
    /// Specific provider implementations might need to adapt this or use their own response models.
    /// </summary>
    public class EmbeddingResponse
    {
        /// <summary>
        /// The type of object returned, typically "list".
        /// </summary>
        [JsonPropertyName("object")]
        public string? ObjectType { get; set; }

        /// <summary>
        /// A list of embedding data objects.
        /// </summary>
        [JsonPropertyName("data")]
        public List<EmbeddingData> Data { get; set; } = new List<EmbeddingData>();

        /// <summary>
        /// The name of the model used to generate the embeddings.
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        /// <summary>
        /// Usage statistics for the request.
        /// </summary>
        [JsonPropertyName("usage")]
        public EmbeddingUsage? Usage { get; set; }
    }

    /// <summary>
    /// Represents a single embedding data object.
    /// </summary>
    public class EmbeddingData
    {
        /// <summary>
        /// The type of object, typically "embedding".
        /// </summary>
        [JsonPropertyName("object")]
        public string? ObjectType { get; set; }

        /// <summary>
        /// The embedding vector.
        /// </summary>
        [JsonPropertyName("embedding")]
        public List<float> Embedding { get; set; } = new List<float>();

        /// <summary>
        /// The index of this embedding in the original request.
        /// </summary>
        [JsonPropertyName("index")]
        public int Index { get; set; }
    }

    /// <summary>
    /// Represents usage statistics for an embedding request.
    /// </summary>
    public class EmbeddingUsage
    {
        /// <summary>
        /// The number of tokens in the prompt.
        /// </summary>
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        /// <summary>
        /// The total number of tokens consumed by the request.
        /// </summary>
        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }
}
