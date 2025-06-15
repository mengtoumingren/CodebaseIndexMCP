# 文件变更刷新逻辑改进实施计划

## 📋 项目概述

### 改进目标
在检测到文件变更后，先将需要更新的文件路径持久化到本地，等索引更新后，再删除记录。如果更新过程由于意外中断服务，重启后，根据记录的变更文件信息继续更新索引。

### 核心问题
1. **可靠性不足**：当前文件变更处理缺乏持久化保障，服务中断会导致变更丢失
2. **无法恢复**：服务重启后无法恢复未完成的文件索引更新
3. **处理时机不可控**：批处理依赖定时器，无法确保变更一定被处理完成

### 解决方案
- **先持久化后处理**：文件变更检测后立即保存到本地存储
- **确保完整性**：只有索引更新成功后才删除持久化记录  
- **支持断点续传**：服务重启时自动恢复未完成的变更处理

---

## 🎯 技术架构设计

### 核心组件

#### 1. FileChangePersistenceService
```csharp
/// <summary>
/// 文件变更持久化服务 - 管理文件变更事件的本地存储和恢复
/// </summary>
public class FileChangePersistenceService
{
    // 保存变更事件到本地存储
    Task<bool> SaveChangeAsync(FileChangeEvent change);
    
    // 更新变更事件状态
    Task<bool> UpdateChangeAsync(FileChangeEvent change);
    
    // 加载所有待处理的变更
    Task<List<FileChangeEvent>> LoadPendingChangesAsync();
    
    // 加载正在处理中的变更
    Task<List<FileChangeEvent>> LoadProcessingChangesAsync();
    
    // 清理已完成的变更记录
    Task<bool> CleanupChangeAsync(string changeId);
    
    // 定期清理过期记录
    Task<int> CleanupExpiredChangesAsync(TimeSpan maxAge);
}
```

#### 2. 扩展的 FileChangeEvent 模型
```csharp
public class FileChangeEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FilePath { get; set; } = string.Empty;
    public FileChangeType ChangeType { get; set; }
    public DateTime Timestamp { get; set; }
    public string CollectionName { get; set; } = string.Empty;
    
    // 🔥 新增字段
    public FileChangeStatus Status { get; set; } = FileChangeStatus.Pending;
    public DateTime? ProcessedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; } = 0;
    public DateTime? LastRetryAt { get; set; }
}

public enum FileChangeStatus
{
    Pending,    // 等待处理
    Processing, // 正在处理
    Completed,  // 处理完成
    Failed,     // 处理失败
    Expired     // 过期失效
}
```

### 存储策略
- **存储位置**：`file-changes-storage/` 目录（可通过配置修改）
- **文件格式**：JSON格式，每个变更事件对应一个文件
- **文件命名**：`{changeId}.json`
- **索引文件**：`changes-index.json`（可选，加速查询）

---

## 🚀 实施阶段

### **阶段一：创建文件变更持久化服务** (预计 1-2 天)

#### 1.1 创建 FileChangePersistenceService.cs
```csharp
namespace CodebaseMcpServer.Services;

public class FileChangePersistenceService
{
    private readonly string _storePath;
    private readonly ILogger<FileChangePersistenceService> _logger;
    private readonly object _fileLock = new object();

    public FileChangePersistenceService(
        ILogger<FileChangePersistenceService> logger, 
        IConfiguration configuration)
    {
        _logger = logger;
        var baseDir = configuration.GetValue<string>("FileChangePersistence:StorageDirectory") 
            ?? "file-changes-storage";
        _storePath = Path.Combine(Directory.GetCurrentDirectory(), baseDir);
        
        Directory.CreateDirectory(_storePath);
        _logger.LogInformation("文件变更持久化存储目录: {Path}", _storePath);
    }

    public async Task<bool> SaveChangeAsync(FileChangeEvent change)
    {
        try
        {
            var changeFile = Path.Combine(_storePath, $"{change.Id}.json");
            var changeData = new PersistedFileChange
            {
                Change = change,
                SavedAt = DateTime.UtcNow,
                Version = "1.0"
            };

            var json = JsonSerializer.Serialize(changeData, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            lock (_fileLock)
            {
                File.WriteAllText(changeFile, json);
            }

            _logger.LogDebug("文件变更已持久化: {Id} - {Path}", change.Id, change.FilePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存文件变更失败: {Id}", change.Id);
            return false;
        }
    }
    
    // ... 其他方法实现
}

public class PersistedFileChange
{
    public FileChangeEvent Change { get; set; } = new();
    public DateTime SavedAt { get; set; }
    public string Version { get; set; } = "1.0";
}
```

#### 1.2 扩展 FileChangeEvent 模型
在 `Models/FileChangeEvent.cs` 中添加新的属性和枚举。

#### 1.3 配置文件更新
在 `appsettings.json` 中添加：
```json
{
  "FileChangePersistence": {
    "StorageDirectory": "file-changes-storage",
    "CleanupIntervalHours": 24,
    "MaxRetryAttempts": 3,
    "RetryDelayMinutes": 5,
    "MaxAgeHours": 168
  }
}
```

#### 1.4 服务注册
在 `Program.cs` 中注册新服务：
```csharp
builder.Services.AddSingleton<FileChangePersistenceService>();
```

### **阶段二：改进 FileWatcherService** (预计 1-2 天)

#### 2.1 注入文件变更持久化服务
```csharp
public class FileWatcherService : BackgroundService
{
    private readonly FileChangePersistenceService _fileChangePersistence;
    
    public FileWatcherService(
        // ... 现有参数
        FileChangePersistenceService fileChangePersistence)
    {
        // ... 现有初始化
        _fileChangePersistence = fileChangePersistence;
    }
}
```

#### 2.2 重构文件变更检测逻辑
```csharp
private void OnFileChanged(CodebaseMapping mapping, FileSystemEventArgs e, FileChangeType changeType)
{
    try
    {
        // ... 现有的文件过滤逻辑保持不变

        // 🔥 核心改进：创建变更事件并立即持久化
        var changeEvent = new FileChangeEvent
        {
            FilePath = e.FullPath,
            ChangeType = changeType,
            Timestamp = DateTime.UtcNow,
            CollectionName = mapping.CollectionName,
            Status = FileChangeStatus.Pending
        };

        // 异步持久化，不阻塞文件监控
        _ = Task.Run(async () => await PersistFileChange(changeEvent));
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "处理文件变更事件失败: {Path}", e.FullPath);
    }
}

private async Task PersistFileChange(FileChangeEvent changeEvent)
{
    try
    {
        await _fileChangePersistence.SaveChangeAsync(changeEvent);
        _logger.LogDebug("文件变更已持久化: {Id} - {Path}", 
            changeEvent.Id, changeEvent.FilePath);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "持久化文件变更失败: {Path}", changeEvent.FilePath);
        
        // 🔄 降级策略：持久化失败时仍尝试直接处理（向后兼容）
        lock (_pendingChanges)
        {
            if (!_pendingChanges.ContainsKey(changeEvent.CollectionName))
            {
                _pendingChanges[changeEvent.CollectionName] = new List<FileChangeEvent>();
            }
            _pendingChanges[changeEvent.CollectionName].Add(changeEvent);
        }
    }
}
```

#### 2.3 重构批处理逻辑
```csharp
private void ProcessPendingChanges(object? state)
{
    // 🔥 改进：从持久化存储加载待处理的变更
    _ = Task.Run(async () => await ProcessPersistedChanges());
}

private async Task ProcessPersistedChanges()
{
    try
    {
        var pendingChanges = await _fileChangePersistence.LoadPendingChangesAsync();
        
        if (pendingChanges.Count == 0)
            return;

        _logger.LogInformation("发现 {Count} 个待处理的文件变更", pendingChanges.Count);

        // 按集合分组并去重处理
        var groupedChanges = pendingChanges
            .GroupBy(c => c.CollectionName)
            .ToDictionary(g => g.Key, g => DeduplicateChanges(g.ToList()));
        
        foreach (var kvp in groupedChanges)
        {
            await ProcessCollectionPersistedChanges(kvp.Key, kvp.Value);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "处理持久化文件变更失败");
    }
}

private List<FileChangeEvent> DeduplicateChanges(List<FileChangeEvent> changes)
{
    // 同一文件的多次变更只保留最新的
    return changes
        .GroupBy(c => c.FilePath)
        .Select(g => g.OrderByDescending(c => c.Timestamp).First())
        .ToList();
}
```

### **阶段三：增强处理确认机制** (预计 1 天)

#### 3.1 重构单个文件变更处理
```csharp
private async Task<bool> ProcessSingleFileChange(FileChangeEvent change)
{
    try
    {
        // 🔥 关键改进：标记为处理中并持久化状态
        change.Status = FileChangeStatus.Processing;
        await _fileChangePersistence.UpdateChangeAsync(change);

        bool success = false;
        string errorMessage = string.Empty;

        switch (change.ChangeType)
        {
            case FileChangeType.Created:
            case FileChangeType.Modified:
                if (File.Exists(change.FilePath))
                {
                    var taskManager = GetTaskManager();
                    success = await taskManager.UpdateFileIndexAsync(change.FilePath, change.CollectionName);
                    if (!success) errorMessage = "文件索引更新失败";
                }
                else
                {
                    errorMessage = "文件不存在，跳过处理";
                    success = true; // 文件不存在视为成功（可能已被删除）
                }
                break;

            case FileChangeType.Deleted:
                var taskManagerForDelete = GetTaskManager();
                success = await taskManagerForDelete.HandleFileDeletionAsync(change.FilePath, change.CollectionName);
                if (!success) errorMessage = "删除文件索引失败";
                break;

            case FileChangeType.Renamed:
                success = true; // 重命名通过删除+创建事件处理
                break;
        }

        // 🔥 核心改进：根据处理结果更新状态
        if (success)
        {
            change.Status = FileChangeStatus.Completed;
            change.ProcessedAt = DateTime.UtcNow;
            await _fileChangePersistence.UpdateChangeAsync(change);
            
            // 🎯 关键：处理成功后立即删除持久化记录
            await _fileChangePersistence.CleanupChangeAsync(change.Id);
            
            _logger.LogDebug("文件变更处理完成并清理: {Id} - {Path}", change.Id, change.FilePath);
        }
        else
        {
            change.Status = FileChangeStatus.Failed;
            change.ErrorMessage = errorMessage;
            change.RetryCount++;
            change.LastRetryAt = DateTime.UtcNow;
            await _fileChangePersistence.UpdateChangeAsync(change);
            
            _logger.LogWarning("文件变更处理失败: {Id} - {Path} - {Error}", 
                change.Id, change.FilePath, errorMessage);
        }

        return success;
    }
    catch (Exception ex)
    {
        // 🔥 关键改进：异常时也要更新状态
        change.Status = FileChangeStatus.Failed;
        change.ErrorMessage = ex.Message;
        change.RetryCount++;
        change.LastRetryAt = DateTime.UtcNow;
        await _fileChangePersistence.UpdateChangeAsync(change);
        
        _logger.LogError(ex, "处理文件变更异常: {Id} - {Path}", change.Id, change.FilePath);
        return false;
    }
}
```

### **阶段四：服务启动恢复机制** (预计 0.5 天)

#### 4.1 在服务启动时恢复未完成变更
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    _logger.LogInformation("文件监控服务启动");
    
    try
    {
        // 🔥 关键改进：启动时先恢复未完成的变更
        await RecoverPendingChanges();
        
        // 初始化已配置的监控
        await InitializeWatchers();
        
        // 启动定期清理任务
        _ = Task.Run(() => StartPeriodicCleanup(stoppingToken));
        
        // 等待取消信号
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
    catch (OperationCanceledException)
    {
        _logger.LogInformation("文件监控服务正在停止");
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "文件监控服务运行时发生错误");
    }
    finally
    {
        DisposeWatchers();
    }
}

private async Task RecoverPendingChanges()
{
    try
    {
        // 加载待处理和处理中的变更
        var pendingChanges = await _fileChangePersistence.LoadPendingChangesAsync();
        var processingChanges = await _fileChangePersistence.LoadProcessingChangesAsync();
        
        // 🔄 将正在处理的变更重置为待处理状态
        foreach (var change in processingChanges)
        {
            change.Status = FileChangeStatus.Pending;
            change.ErrorMessage = "服务重启，重新排队处理";
            await _fileChangePersistence.UpdateChangeAsync(change);
        }
        
        var totalRecovered = pendingChanges.Count + processingChanges.Count;
        if (totalRecovered > 0)
        {
            _logger.LogInformation("服务启动时恢复了 {Count} 个未完成的文件变更", totalRecovered);
            
            // 立即触发一次处理
            _ = Task.Run(async () => 
            {
                await Task.Delay(2000); // 等待服务完全启动
                await ProcessPersistedChanges();
            });
        }
        else
        {
            _logger.LogInformation("没有发现需要恢复的文件变更");
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "恢复待处理文件变更失败");
    }
}

private async Task StartPeriodicCleanup(CancellationToken cancellationToken)
{
    var cleanupInterval = _configuration.GetValue<int>("FileChangePersistence:CleanupIntervalHours", 24);
    var maxAge = TimeSpan.FromHours(_configuration.GetValue<int>("FileChangePersistence:MaxAgeHours", 168));
    
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(TimeSpan.FromHours(cleanupInterval), cancellationToken);
            
            var cleanedCount = await _fileChangePersistence.CleanupExpiredChangesAsync(maxAge);
            if (cleanedCount > 0)
            {
                _logger.LogInformation("定期清理了 {Count} 个过期的文件变更记录", cleanedCount);
            }
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "定期清理任务失败");
        }
    }
}
```

---

## 🔧 配置和部署

### 配置文件更新 (appsettings.json)
```json
{
  "FileChangePersistence": {
    "StorageDirectory": "file-changes-storage",
    "CleanupIntervalHours": 24,
    "MaxRetryAttempts": 3,
    "RetryDelayMinutes": 5,
    "MaxAgeHours": 168,
    "EnablePersistence": true
  },
  "FileWatcher": {
    "BatchProcessingDelay": 5000,
    "LogFileChanges": true,
    "EnableAutoMonitoring": true,
    "EnableRecovery": true
  }
}
```

### 服务注册更新 (Program.cs)
```csharp
// 注册文件变更持久化服务
builder.Services.AddSingleton<FileChangePersistenceService>();

// FileWatcherService 将自动注入新的依赖
builder.Services.AddSingleton<FileWatcherService>();
```

---

## 📊 测试验证

### 功能测试用例

#### 1. 基础持久化测试
- ✅ 文件变更检测后能正确持久化到本地存储
- ✅ 变更状态能正确更新（Pending → Processing → Completed）
- ✅ 处理成功后持久化记录被正确删除

#### 2. 故障恢复测试
- ✅ 服务中断后重启能恢复未完成的变更
- ✅ 处理中的变更能重置为待处理状态
- ✅ 恢复的变更能正确执行索引更新

#### 3. 异常处理测试
- ✅ 持久化失败时有降级处理机制
- ✅ 索引更新失败时状态正确标记
- ✅ 重试机制按配置正确执行

#### 4. 性能测试
- ✅ 大量文件变更时系统响应正常
- ✅ 持久化操作不阻塞文件监控
- ✅ 批处理效率符合预期

### 测试脚本示例
```csharp
// 创建测试文件变更
var testFile = Path.Combine(testDirectory, "test.cs");
File.WriteAllText(testFile, "// test content");

// 等待变更被检测和持久化
await Task.Delay(1000);

// 验证持久化记录存在
var changes = await persistenceService.LoadPendingChangesAsync();
Assert.True(changes.Any(c => c.FilePath == testFile));

// 模拟服务重启
// 验证恢复功能
```

---

## 🎯 预期收益

### 可靠性提升
- **零丢失保证**：所有文件变更都有持久化记录，服务中断不会丢失
- **断点续传**：服务重启后自动恢复未完成的处理
- **状态追踪**：完整的变更处理生命周期管理

### 运维能力
- **故障诊断**：通过持久化记录分析处理失败原因
- **性能监控**：统计变更处理成功率、耗时等指标
- **手动干预**：必要时可手动重新处理特定变更

### 系统健壮性
- **服务容错**：网络、数据库等问题不影响变更记录
- **资源隔离**：文件变更和索引任务持久化独立管理
- **向后兼容**：保留现有处理逻辑作为降级方案

### 扩展性
- **监控集成**：可与监控系统集成实现告警
- **批量操作**：支持批量重新处理失败的变更
- **多实例支持**：为将来多实例部署预留接口

---

## 📅 实施时间表

| 阶段 | 任务 | 预计时间 | 关键交付物 |
|-----|------|---------|-----------|
| 阶段一 | 创建持久化服务 | 1-2天 | FileChangePersistenceService, 模型扩展 |
| 阶段二 | 改进监控服务 | 1-2天 | 重构 FileWatcherService 处理逻辑 |
| 阶段三 | 增强确认机制 | 1天 | 完善状态管理和错误处理 |
| 阶段四 | 恢复机制实现 | 0.5天 | 服务启动恢复和定期清理 |
| 测试验证 | 功能和性能测试 | 1天 | 测试报告和性能评估 |

**总计预估时间：4.5-5.5天**

---

## 🚨 风险评估与缓解

### 潜在风险
1. **磁盘空间占用**：持久化文件可能占用较多磁盘空间
   - 缓解：定期清理机制 + 配置最大保留时间
2. **性能影响**：额外的IO操作可能影响性能
   - 缓解：异步处理 + 批量操作优化
3. **并发安全**：多线程访问持久化存储
   - 缓解：文件锁机制 + 线程安全设计

### Rollback 计划
- 保留现有处理逻辑作为降级开关
- 可通过配置禁用持久化功能回退到原始模式
- 渐进式部署，先在测试环境验证稳定性

---

## 📚 参考资料

- 现有 TaskPersistenceService 实现模式
- FileWatcherService 当前架构
- .NET 文件系统监控最佳实践
- JSON 序列化性能优化指南

---

*本文档版本: 1.0*  
*创建时间: 2025-06-15*  
*最后更新: 2025-06-15*