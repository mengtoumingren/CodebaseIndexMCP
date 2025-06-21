using System.Collections.Concurrent;
using CodebaseMcpServer.Models;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Services.Embedding;

/// <summary>
/// 并发嵌入向量管理器 - 核心并发调度组件
/// </summary>
public class ConcurrentEmbeddingManager : IDisposable
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ConcurrencySettings _settings;
    private readonly ILogger<ConcurrentEmbeddingManager> _logger;
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly ConcurrentQueue<string> _processingLog;
    private bool _disposed = false;

    public ConcurrentEmbeddingManager(
        IEmbeddingProvider embeddingProvider,
        ConcurrencySettings settings,
        ILogger<ConcurrentEmbeddingManager> logger)
    {
        _embeddingProvider = embeddingProvider ?? throw new ArgumentNullException(nameof(embeddingProvider));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        if (!_settings.IsValid())
        {
            throw new ArgumentException("Invalid concurrency settings", nameof(settings));
        }
        
        _concurrencyLimiter = new SemaphoreSlim(
            _settings.MaxConcurrentEmbeddingRequests, 
            _settings.MaxConcurrentEmbeddingRequests);
        
        _processingLog = new ConcurrentQueue<string>();
        
        _logger.LogInformation("ConcurrentEmbeddingManager 初始化完成，最大并发数：{MaxConcurrent}，提供商：{Provider}",
            _settings.MaxConcurrentEmbeddingRequests, _embeddingProvider.ProviderName);
    }

    /// <summary>
    /// 并发获取嵌入向量
    /// </summary>
    public async Task<List<List<float>>> GetEmbeddingsConcurrentlyAsync(
        List<string> texts, 
        CancellationToken cancellationToken = default)
    {
        if (texts == null || !texts.Any()) 
        {
            return new List<List<float>>();
        }

        var startTime = DateTime.UtcNow;
        var totalTexts = texts.Count;
        
        LogIfEnabled($"开始并发处理 {totalTexts} 个文本");

        try
        {
            // 智能分割批次
            var batches = SplitIntoConcurrentBatches(texts);
            var allEmbeddings = new List<List<float>>();
            
            LogIfEnabled($"文本分为 {batches.Count} 个并发批次");

            // 并发处理所有批次
            var semaphore = new SemaphoreSlim(_settings.MaxConcurrentEmbeddingRequests);
            var tasks = batches.Select(async (batch, index) =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    LogIfEnabled($"开始处理批次 {index + 1}/{batches.Count}，包含 {batch.Count} 个文本");
                    return await ProcessBatchWithRetry(batch, index + 1, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var results = await Task.WhenAll(tasks);

            // 合并结果，保持原始顺序
            foreach (var batchResult in results)
            {
                allEmbeddings.AddRange(batchResult);
            }

            var processingTime = (DateTime.UtcNow - startTime).TotalSeconds;
            LogIfEnabled($"并发嵌入向量处理完成，获得 {allEmbeddings.Count} 个向量，耗时 {processingTime:F2} 秒");

            return allEmbeddings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "并发嵌入向量处理失败");
            throw;
        }
    }

    /// <summary>
    /// 智能分割文本为并发批次
    /// </summary>
    private List<List<string>> SplitIntoConcurrentBatches(List<string> texts)
    {
        var providerBatchSize = _embeddingProvider.GetMaxBatchSize();
        var optimalBatchSize = _settings.EnableDynamicBatchSizing 
            ? Math.Min(providerBatchSize, _settings.EmbeddingBatchSizeOptimal)
            : _settings.EmbeddingBatchSizeOptimal;

        // 确保批次大小不超过提供商限制
        optimalBatchSize = Math.Min(optimalBatchSize, providerBatchSize);
        optimalBatchSize = Math.Max(1, optimalBatchSize); // 至少为1

        var batches = new List<List<string>>();
        for (int i = 0; i < texts.Count; i += optimalBatchSize)
        {
            var batch = texts.Skip(i).Take(optimalBatchSize).ToList();
            batches.Add(batch);
        }

        LogIfEnabled($"文本分割策略：提供商批次大小={providerBatchSize}，最优批次大小={optimalBatchSize}，实际批次数={batches.Count}");

        return batches;
    }

    /// <summary>
    /// 带重试机制的批次处理
    /// </summary>
    private async Task<List<List<float>>> ProcessBatchWithRetry(
        List<string> batch, 
        int batchNumber,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        var batchStartTime = DateTime.UtcNow;

        for (int attempt = 1; attempt <= _settings.MaxRetryAttempts + 1; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var embeddings = await _embeddingProvider.GetEmbeddingsAsync(batch);

                if (embeddings.Count == batch.Count)
                {
                    var processingTime = (DateTime.UtcNow - batchStartTime).TotalSeconds;
                    LogIfEnabled($"批次 {batchNumber} 处理成功：{batch.Count} 个文本，尝试次数：{attempt}，耗时：{processingTime:F2}秒");
                    return embeddings;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"嵌入向量数量不匹配：期望 {batch.Count}，实际 {embeddings.Count}");
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                var shouldRetry = attempt <= _settings.MaxRetryAttempts;
                
                if (shouldRetry)
                {
                    var delay = CalculateRetryDelay(attempt);
                    _logger.LogWarning("批次 {BatchNumber} 处理失败（尝试 {Attempt}/{MaxAttempts}），{Delay}ms后重试：{Error}",
                        batchNumber, attempt, _settings.MaxRetryAttempts + 1, delay, ex.Message);
                    
                    await Task.Delay(delay, cancellationToken);
                }
                else
                {
                    _logger.LogError(lastException, "批次 {BatchNumber} 处理最终失败：{BatchSize} 个文本",
                        batchNumber, batch.Count);
                }
            }
        }

        // 处理失败后的回退策略
        if (_settings.EnableFailureFallback)
        {
            LogIfEnabled($"批次 {batchNumber} 启用失败回退机制，返回零向量");
            var dimension = _embeddingProvider.GetEmbeddingDimension();
            return batch.Select(_ => 
                Enumerable.Repeat(0.0f, dimension).ToList()).ToList();
        }

        throw lastException ?? new Exception($"批次 {batchNumber} 处理失败");
    }

    /// <summary>
    /// 计算重试延迟（指数退避）
    /// </summary>
    private int CalculateRetryDelay(int attempt)
    {
        // 指数退避：baseDelay * 2^(attempt-1)
        var baseDelay = _settings.RetryDelayMs;
        var exponentialDelay = baseDelay * Math.Pow(2, attempt - 1);
        
        // 限制最大延迟为30秒
        return (int)Math.Min(exponentialDelay, 30000);
    }

    /// <summary>
    /// 获取处理统计信息
    /// </summary>
    public ConcurrencyStatistics GetStatistics()
    {
        return new ConcurrencyStatistics
        {
            MaxConcurrentRequests = _settings.MaxConcurrentEmbeddingRequests,
            EmbeddingProvider = _embeddingProvider.ProviderName,
            ProcessingLogCount = _processingLog.Count,
            LastUpdated = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 条件性日志记录
    /// </summary>
    private void LogIfEnabled(string message)
    {
        if (_settings.EnableConcurrencyLogging)
        {
            _logger.LogDebug("[ConcurrentEmbeddingManager] {Message}", message);
            _processingLog.Enqueue($"{DateTime.UtcNow:HH:mm:ss.fff} - {message}");
            
            // 限制日志队列大小
            while (_processingLog.Count > 1000)
            {
                _processingLog.TryDequeue(out _);
            }
        }
    }

    /// <summary>
    /// 获取处理日志
    /// </summary>
    public List<string> GetProcessingLog()
    {
        return _processingLog.ToList();
    }

    /// <summary>
    /// 清理处理日志
    /// </summary>
    public void ClearProcessingLog()
    {
        while (_processingLog.TryDequeue(out _)) { }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _concurrencyLimiter?.Dispose();
            _disposed = true;
            _logger.LogDebug("ConcurrentEmbeddingManager 已释放资源");
        }
    }
}

/// <summary>
/// 并发处理统计信息
/// </summary>
public class ConcurrencyStatistics
{
    public int MaxConcurrentRequests { get; set; }
    public string EmbeddingProvider { get; set; } = "";
    public int ProcessingLogCount { get; set; }
    public DateTime LastUpdated { get; set; }
}