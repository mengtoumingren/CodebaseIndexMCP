using System.Text.Json;
using CodebaseMcpServer.Models;
using CodebaseMcpServer.Extensions;

namespace CodebaseMcpServer.Services;

/// <summary>
/// 索引配置管理器 - 管理 codebase-indexes.json 配置文件
/// </summary>
public class IndexConfigManager
{
    private const string CONFIG_FILE = "codebase-indexes.json";
    private readonly string _configPath;
    private IndexConfiguration _config;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ILogger<IndexConfigManager> _logger;
    
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IndexConfigManager(ILogger<IndexConfigManager> logger)
    {
        _logger = logger;
        _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CONFIG_FILE);
        _config = LoadConfiguration();
    }

    /// <summary>
    /// 加载配置文件
    /// </summary>
    private IndexConfiguration LoadConfiguration()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                _logger.LogInformation("配置文件不存在，创建默认配置: {Path}", _configPath);
                var defaultConfig = CreateDefaultConfiguration();
                SaveConfigurationInternal(defaultConfig);
                return defaultConfig;
            }

            var json = File.ReadAllText(_configPath);
            var config = JsonSerializer.Deserialize<IndexConfiguration>(json, _jsonOptions);
            
            if (config == null)
            {
                _logger.LogWarning("配置文件解析失败，使用默认配置");
                return CreateDefaultConfiguration();
            }

            _logger.LogInformation("成功加载配置文件: {Path}, 包含 {Count} 个代码库映射", 
                _configPath, config.CodebaseMappings.Count);
            
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载配置文件失败: {Path}", _configPath);
            return CreateDefaultConfiguration();
        }
    }

    /// <summary>
    /// 创建默认配置
    /// </summary>
    private static IndexConfiguration CreateDefaultConfiguration()
    {
        return new IndexConfiguration
        {
            Version = "1.0",
            LastUpdated = DateTime.UtcNow,
            CodebaseMappings = new List<CodebaseMapping>(),
            GlobalSettings = new GlobalSettings()
        };
    }

    /// <summary>
    /// 保存配置文件
    /// </summary>
    private async Task SaveConfiguration()
    {
        await _fileLock.WaitAsync();
        try
        {
            _config.LastUpdated = DateTime.UtcNow;
            await SaveConfigurationInternal(_config);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 内部保存配置方法
    /// </summary>
    private async Task SaveConfigurationInternal(IndexConfiguration config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(_configPath, json);
            _logger.LogDebug("配置文件已保存: {Path}", _configPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存配置文件失败: {Path}", _configPath);
            throw;
        }
    }

    /// <summary>
    /// 添加代码库映射
    /// </summary>
    public async Task<bool> AddCodebaseMapping(CodebaseMapping mapping)
    {
        await _fileLock.WaitAsync();
        try
        {
            // 检查是否已存在相同路径的映射
            if (_config.CodebaseMappings.Any(m => 
                m.NormalizedPath.Equals(mapping.NormalizedPath, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("代码库映射已存在: {Path}", mapping.CodebasePath);
                return false;
            }

            _config.CodebaseMappings.Add(mapping);
            _config.LastUpdated = DateTime.UtcNow;
            await SaveConfigurationInternal(_config);
            
            _logger.LogInformation("添加代码库映射: {Path} -> {Collection}", 
                mapping.CodebasePath, mapping.CollectionName);
            
            return true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 根据路径获取映射
    /// </summary>
    public CodebaseMapping? GetMappingByPath(string path)
    {
        var normalizedPath = path.NormalizePath();
        return _config.CodebaseMappings.FirstOrDefault(m => 
            m.NormalizedPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 根据集合名称获取映射
    /// </summary>
    public CodebaseMapping? GetMappingByCollection(string collectionName)
    {
        return _config.CodebaseMappings.FirstOrDefault(m => 
            m.CollectionName.Equals(collectionName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 更新映射信息
    /// </summary>
    public async Task<bool> UpdateMapping(CodebaseMapping updatedMapping)
    {
        await _fileLock.WaitAsync();
        try
        {
            var existingMapping = _config.CodebaseMappings.FirstOrDefault(m => m.Id == updatedMapping.Id);
            if (existingMapping == null)
            {
                _logger.LogWarning("要更新的映射不存在: {Id}", updatedMapping.Id);
                return false;
            }

            // 更新映射信息
            var index = _config.CodebaseMappings.IndexOf(existingMapping);
            _config.CodebaseMappings[index] = updatedMapping;
            
            _config.LastUpdated = DateTime.UtcNow;
            await SaveConfigurationInternal(_config);
            
            _logger.LogInformation("更新代码库映射: {Path} -> {Collection}", 
                updatedMapping.CodebasePath, updatedMapping.CollectionName);
            
            return true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 删除映射
    /// </summary>
    public async Task<bool> RemoveMapping(string id)
    {
        await _fileLock.WaitAsync();
        try
        {
            var mapping = _config.CodebaseMappings.FirstOrDefault(m => m.Id == id);
            if (mapping == null)
            {
                _logger.LogWarning("要删除的映射不存在: {Id}", id);
                return false;
            }

            _config.CodebaseMappings.Remove(mapping);
            _config.LastUpdated = DateTime.UtcNow;
            await SaveConfigurationInternal(_config);
            
            _logger.LogInformation("删除代码库映射: {Path} -> {Collection}", 
                mapping.CodebasePath, mapping.CollectionName);
            
            return true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 获取所有映射
    /// </summary>
    public List<CodebaseMapping> GetAllMappings()
    {
        return new List<CodebaseMapping>(_config.CodebaseMappings);
    }

    /// <summary>
    /// 获取需要监控的映射
    /// </summary>
    public List<CodebaseMapping> GetMonitoredMappings()
    {
        return _config.CodebaseMappings
            .Where(m => m.IsMonitoring && m.IndexingStatus == "completed")
            .ToList();
    }

    /// <summary>
    /// 获取完整配置
    /// </summary>
    public async Task<IndexConfiguration> GetConfiguration()
    {
        await _fileLock.WaitAsync();
        try
        {
            return new IndexConfiguration
            {
                Version = _config.Version,
                LastUpdated = _config.LastUpdated,
                CodebaseMappings = new List<CodebaseMapping>(_config.CodebaseMappings),
                GlobalSettings = _config.GlobalSettings
            };
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 更新映射统计信息
    /// </summary>
    public async Task<bool> UpdateMappingStatistics(string id, Action<IndexStatistics> updateAction)
    {
        await _fileLock.WaitAsync();
        try
        {
            var mapping = _config.CodebaseMappings.FirstOrDefault(m => m.Id == id);
            if (mapping == null)
            {
                return false;
            }

            updateAction(mapping.Statistics);
            _config.LastUpdated = DateTime.UtcNow;
            await SaveConfigurationInternal(_config);
            return true;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _fileLock?.Dispose();
    }
}