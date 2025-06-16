namespace CodebaseMcpServer.Models
{
    /// <summary>
    /// Specifies the types of available embedding providers.
    /// </summary>
    public enum EmbeddingProviderType
    {
        /// <summary>
        /// Alibaba Cloud DashScope
        /// </summary>
        DashScope,

        /// <summary>
        /// OpenAI official API
        /// </summary>
        OpenAI,

        /// <summary>
        /// Azure OpenAI Service
        /// </summary>
        AzureOpenAI,

        /// <summary>
        /// Hugging Face Inference API
        /// </summary>
        HuggingFace,

        /// <summary>
        /// Ollama本地嵌入服务
        /// </summary>
        Ollama

        // Add more providers here as needed in the future.
        // Example:
        // /// <summary>
        // /// Google Vertex AI
        // /// </summary>
        // GoogleVertexAI,
        //
        // /// <summary>
        // /// Amazon Bedrock
        // /// </summary>
        // AmazonBedrock
    }
}