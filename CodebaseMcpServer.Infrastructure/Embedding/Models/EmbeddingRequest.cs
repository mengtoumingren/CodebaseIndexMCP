using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CodebaseMcpServer.Infrastructure.Embedding.Models
{
    /// <summary>
    /// Represents a request to an embedding provider.
    /// This is a generic model that might be adapted by specific provider implementations.
    /// </summary>
    public class EmbeddingRequest
    {
        /// <summary>
        /// The model to use for generating embeddings.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model { get; set; }

        /// <summary>
        /// The list of input texts to embed.
        /// </summary>
        [JsonPropertyName("input")]
        public List<string> Input { get; set; }

        /// <summary>
        /// Optional: The format of the returned embeddings.
        /// Not all providers support this. Example: "float"
        /// </summary>
        [JsonPropertyName("encoding_format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EncodingFormat { get; set; }

        /// <summary>
        /// Optional: The number of dimensions the resulting output embeddings should have.
        /// Not all providers support this.
        /// </summary>
        // Dimensions is already nullable (int?), so it's fine.
        [JsonPropertyName("dimensions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Dimensions { get; set; }

        /// <summary>
        /// Optional: A unique identifier representing your end-user, which can help providers monitor and detect abuse.
        /// </summary>
        [JsonPropertyName("user")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? User { get; set; }

        public EmbeddingRequest(string model, List<string> input)
        {
            Model = model;
            Input = input;
        }
    }
}
