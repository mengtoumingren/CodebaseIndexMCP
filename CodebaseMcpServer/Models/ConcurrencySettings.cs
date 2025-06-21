using System.ComponentModel.DataAnnotations;

namespace CodebaseMcpServer.Models;

/// <summary>
/// 并发处理配置设置
/// </summary>
public class ConcurrencySettings
{
    /// <summary>
    /// 最大并发嵌入向量请求数
    /// </summary>
    [Range(1, 20)]
    public int MaxConcurrentEmbeddingRequests { get; set; } = 4;
    
    /// <summary>
    /// 最大并发文件批次数
    /// </summary>
    [Range(1, 10)]
    public int MaxConcurrentFileBatches { get; set; } = 2;
    
    /// <summary>
    /// 嵌入向量最优批次大小
    /// </summary>
    [Range(1, 100)]
    public int EmbeddingBatchSizeOptimal { get; set; } = 10;
    
    /// <summary>
    /// 网络请求超时时间（毫秒）
    /// </summary>
    [Range(5000, 120000)]
    public int NetworkTimeoutMs { get; set; } = 30000;
    
    /// <summary>
    /// 启用动态批次大小调整
    /// </summary>
    public bool EnableDynamicBatchSizing { get; set; } = true;
    
    /// <summary>
    /// 启用失败回退机制
    /// </summary>
    public bool EnableFailureFallback { get; set; } = true;
    
    /// <summary>
    /// 重试次数
    /// </summary>
    [Range(0, 10)]
    public int MaxRetryAttempts { get; set; } = 3;
    
    /// <summary>
    /// 重试延迟（毫秒）
    /// </summary>
    [Range(100, 10000)]
    public int RetryDelayMs { get; set; } = 1000;
    
    /// <summary>
    /// 启用并发处理日志
    /// </summary>
    public bool EnableConcurrencyLogging { get; set; } = true;
    
    /// <summary>
    /// 验证配置参数的合理性
    /// </summary>
    public bool IsValid()
    {
        return MaxConcurrentEmbeddingRequests > 0 &&
               MaxConcurrentFileBatches > 0 &&
               EmbeddingBatchSizeOptimal > 0 &&
               NetworkTimeoutMs > 0 &&
               MaxRetryAttempts >= 0 &&
               RetryDelayMs > 0;
    }
    
    /// <summary>
    /// 根据硬件环境调整配置
    /// </summary>
    public void OptimizeForEnvironment()
    {
        var coreCount = Environment.ProcessorCount;
        
        // 根据CPU核心数调整并发度
        if (coreCount >= 8)
        {
            MaxConcurrentEmbeddingRequests = Math.Min(8, MaxConcurrentEmbeddingRequests);
            MaxConcurrentFileBatches = Math.Min(4, MaxConcurrentFileBatches);
        }
        else if (coreCount >= 4)
        {
            MaxConcurrentEmbeddingRequests = Math.Min(4, MaxConcurrentEmbeddingRequests);
            MaxConcurrentFileBatches = Math.Min(2, MaxConcurrentFileBatches);
        }
        else
        {
            MaxConcurrentEmbeddingRequests = Math.Min(2, MaxConcurrentEmbeddingRequests);
            MaxConcurrentFileBatches = 1;
        }
    }
}