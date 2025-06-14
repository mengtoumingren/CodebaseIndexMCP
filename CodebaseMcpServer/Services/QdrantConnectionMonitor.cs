using Qdrant.Client;
using System.Collections.Concurrent;

namespace CodebaseMcpServer.Services;

/// <summary>
/// Qdrant连接监控服务 - 监控Qdrant服务器连接状态
/// </summary>
public class QdrantConnectionMonitor : IDisposable
{
    private readonly ILogger<QdrantConnectionMonitor> _logger;
    private readonly IConfiguration _configuration;
    private readonly string _qdrantHost;
    private readonly int _qdrantPort;
    private readonly Timer _healthCheckTimer;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _waitingTasks = new();
    
    private bool _isConnected = false;
    private DateTime _lastSuccessfulCheck = DateTime.MinValue;
    private DateTime _lastFailedCheck = DateTime.MinValue;
    private int _consecutiveFailures = 0;
    private QdrantClient? _testClient;

    public event Action<bool>? ConnectionStatusChanged;
    public event Action<string>? ConnectionError;

    public bool IsConnected => _isConnected;
    public DateTime LastSuccessfulCheck => _lastSuccessfulCheck;
    public DateTime LastFailedCheck => _lastFailedCheck;
    public int ConsecutiveFailures => _consecutiveFailures;

    public QdrantConnectionMonitor(
        ILogger<QdrantConnectionMonitor> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        
        _qdrantHost = configuration.GetValue<string>("CodeSearch:QdrantConfig:Host") ?? "localhost";
        _qdrantPort = configuration.GetValue<int>("CodeSearch:QdrantConfig:Port", 6334);
        
        var checkInterval = configuration.GetValue<int>("QdrantMonitor:HealthCheckInterval", 30000); // 默认30秒
        
        _logger.LogInformation("初始化Qdrant连接监控 - 主机: {Host}:{Port}, 检查间隔: {Interval}ms", 
            _qdrantHost, _qdrantPort, checkInterval);
        
        // 创建测试客户端
        _testClient = new QdrantClient(_qdrantHost, _qdrantPort);
        
        // 启动健康检查定时器
        _healthCheckTimer = new Timer(PerformHealthCheck, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(checkInterval));
    }

    /// <summary>
    /// 执行健康检查
    /// </summary>
    private async void PerformHealthCheck(object? state)
    {
        try
        {
            var wasConnected = _isConnected;
            var isCurrentlyConnected = await CheckConnectionAsync();
            
            if (isCurrentlyConnected != wasConnected)
            {
                _isConnected = isCurrentlyConnected;
                _logger.LogInformation("Qdrant连接状态变更: {Status}", _isConnected ? "已连接" : "已断开");
                
                // 触发状态变更事件
                ConnectionStatusChanged?.Invoke(_isConnected);
                
                // 如果连接恢复，通知等待的任务
                if (_isConnected)
                {
                    NotifyWaitingTasks();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "健康检查执行失败");
        }
    }

    /// <summary>
    /// 检查连接状态
    /// </summary>
    private async Task<bool> CheckConnectionAsync()
    {
        try
        {
            if (_testClient == null)
            {
                _testClient = new QdrantClient(_qdrantHost, _qdrantPort);
            }

            // 尝试获取集合列表来测试连接
            var collections = await _testClient.ListCollectionsAsync();
            
            _lastSuccessfulCheck = DateTime.UtcNow;
            _consecutiveFailures = 0;
            
            return true;
        }
        catch (Exception ex)
        {
            _lastFailedCheck = DateTime.UtcNow;
            _consecutiveFailures++;
            
            var errorMessage = $"Qdrant连接失败 (连续失败: {_consecutiveFailures}): {ex.Message}";
            _logger.LogWarning(errorMessage);
            
            // 触发连接错误事件
            ConnectionError?.Invoke(errorMessage);
            
            return false;
        }
    }

    /// <summary>
    /// 等待连接恢复
    /// </summary>
    public async Task<bool> WaitForConnectionAsync(string taskId, TimeSpan? timeout = null)
    {
        if (_isConnected)
        {
            return true;
        }

        var timeoutMs = timeout?.TotalMilliseconds ?? _configuration.GetValue<int>("QdrantMonitor:ConnectionTimeout", 300000); // 默认5分钟
        var tcs = new TaskCompletionSource<bool>();
        
        _waitingTasks.TryAdd(taskId, tcs);
        _logger.LogInformation("任务 {TaskId} 等待Qdrant连接恢复，超时时间: {Timeout}ms", taskId, timeoutMs);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
            cts.Token.Register(() => tcs.TrySetCanceled());
            
            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("任务 {TaskId} 等待连接超时", taskId);
            return false;
        }
        finally
        {
            _waitingTasks.TryRemove(taskId, out _);
        }
    }

    /// <summary>
    /// 通知等待的任务连接已恢复
    /// </summary>
    private void NotifyWaitingTasks()
    {
        var taskIds = _waitingTasks.Keys.ToList();
        foreach (var taskId in taskIds)
        {
            if (_waitingTasks.TryRemove(taskId, out var tcs))
            {
                tcs.TrySetResult(true);
                _logger.LogInformation("通知任务 {TaskId} 连接已恢复", taskId);
            }
        }
    }

    /// <summary>
    /// 取消等待连接的任务
    /// </summary>
    public bool CancelWaitingTask(string taskId)
    {
        if (_waitingTasks.TryRemove(taskId, out var tcs))
        {
            tcs.TrySetCanceled();
            _logger.LogInformation("取消任务 {TaskId} 的连接等待", taskId);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 强制检查连接状态
    /// </summary>
    public async Task<bool> ForceCheckAsync()
    {
        _logger.LogInformation("执行强制连接检查");
        var wasConnected = _isConnected;
        var isCurrentlyConnected = await CheckConnectionAsync();
        
        if (isCurrentlyConnected != wasConnected)
        {
            _isConnected = isCurrentlyConnected;
            _logger.LogInformation("强制检查后连接状态: {Status}", _isConnected ? "已连接" : "已断开");
            ConnectionStatusChanged?.Invoke(_isConnected);
            
            if (_isConnected)
            {
                NotifyWaitingTasks();
            }
        }
        
        return _isConnected;
    }

    /// <summary>
    /// 获取连接状态统计
    /// </summary>
    public async Task<object> GetConnectionStatisticsAsync()
    {
        return new
        {
            IsConnected = _isConnected,
            Host = _qdrantHost,
            Port = _qdrantPort,
            LastSuccessfulCheck = _lastSuccessfulCheck,
            LastFailedCheck = _lastFailedCheck,
            ConsecutiveFailures = _consecutiveFailures,
            WaitingTasks = _waitingTasks.Count,
            WaitingTaskIds = _waitingTasks.Keys.ToList(),
            UptimeStatus = _isConnected ? 
                (_lastSuccessfulCheck == DateTime.MinValue ? "Unknown" : 
                 $"Up for {(DateTime.UtcNow - _lastSuccessfulCheck).TotalMinutes:F1} minutes") :
                (_lastFailedCheck == DateTime.MinValue ? "Unknown" : 
                 $"Down for {(DateTime.UtcNow - _lastFailedCheck).TotalMinutes:F1} minutes"),
            LastUpdated = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        _testClient?.Dispose();
        
        // 取消所有等待的任务
        foreach (var kvp in _waitingTasks)
        {
            kvp.Value.TrySetCanceled();
        }
        _waitingTasks.Clear();
        
        _logger.LogInformation("Qdrant连接监控服务已停止");
    }
}