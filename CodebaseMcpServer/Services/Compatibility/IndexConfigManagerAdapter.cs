using CodebaseMcpServer.Models;
using CodebaseMcpServer.Services.Domain;
using CodebaseMcpServer.Extensions;

namespace CodebaseMcpServer.Services.Compatibility;

/// <summary>
/// IndexConfigManager兼容性适配器 - 让现有MCP工具无缝使用新的IndexLibraryService
/// </summary>
public class IndexConfigManagerAdapter
{
    private readonly IIndexLibraryService _indexLibraryService;
    private readonly ILogger<IndexConfigManagerAdapter> _logger;

    public IndexConfigManagerAdapter(
        IIndexLibraryService indexLibraryService,
        ILogger<IndexConfigManagerAdapter> logger)
    {
        _indexLibraryService = indexLibraryService;
        _logger = logger;
    }

    /// <summary>
    /// 添加代码库映射 - 兼容原有接口
    /// </summary>
    public async Task<bool> AddCodebaseMapping(CodebaseMcpServer.Services.Domain.CodebaseMapping mapping)
    {
        try
        {
            var request = new CreateIndexLibraryRequest
            {
                CodebasePath = mapping.CodebasePath,
                Name = mapping.FriendlyName,
                AutoDetectType = true
            };

            var result = await _indexLibraryService.CreateAsync(request);
            
            if (result.IsSuccess)
            {
                _logger.LogInformation("兼容性适配: 添加代码库映射成功 {Path}", mapping.CodebasePath);
                return true;
            }
            else
            {
                _logger.LogWarning("兼容性适配: 添加代码库映射失败 {Path}: {Message}", 
                    mapping.CodebasePath, result.Message);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "兼容性适配: 添加代码库映射异常 {Path}", mapping.CodebasePath);
            return false;
        }
    }

    /// <summary>
    /// 根据路径获取映射 - 兼容原有接口
    /// </summary>
    public CodebaseMcpServer.Services.Domain.CodebaseMapping? GetMappingByPath(string path)
    {
        try
        {
            var task = _indexLibraryService.GetLegacyMappingByPathAsync(path);
            task.Wait();
            return task.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "兼容性适配: 获取映射失败 {Path}", path);
            return null;
        }
    }

    /// <summary>
    /// 查找路径对应的映射，支持父目录回退查找 - 兼容原有接口
    /// </summary>
    public CodebaseMcpServer.Services.Domain.CodebaseMapping? GetMappingByPathWithParentFallback(string path)
    {
        var normalizedPath = path.NormalizePath();
        
        // 首先尝试直接匹配
        var directMapping = GetMappingByPath(normalizedPath);
        if (directMapping != null)
        {
            _logger.LogDebug("兼容性适配: 找到直接路径映射: {QueryPath} -> {CollectionName}",
                normalizedPath, directMapping.CollectionName);
            return directMapping;
        }
        
        _logger.LogDebug("兼容性适配: 未找到直接路径映射，开始向上查找父目录: {QueryPath}", normalizedPath);
        
        // 如果没有直接匹配，向上查找父目录
        var currentPath = normalizedPath;
        int searchDepth = 0;
        const int maxSearchDepth = 10;
        
        while (!string.IsNullOrEmpty(currentPath) && searchDepth < maxSearchDepth)
        {
            var parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrEmpty(parentPath) || parentPath == currentPath)
            {
                _logger.LogDebug("兼容性适配: 已到达根目录，停止查找");
                break;
            }
            
            searchDepth++;
            var normalizedParentPath = parentPath.NormalizePath();
            
            _logger.LogDebug("兼容性适配: 检查父目录 {Depth}: {ParentPath}", searchDepth, normalizedParentPath);
            
            var parentMapping = GetMappingByPath(normalizedParentPath);
            if (parentMapping != null)
            {
                _logger.LogInformation("兼容性适配: 找到父目录映射: 查询路径 {QueryPath} -> 父索引库 {ParentPath} (集合: {CollectionName})",
                    normalizedPath, parentMapping.CodebasePath, parentMapping.CollectionName);
                return parentMapping;
            }
            
            currentPath = parentPath;
        }
        
        if (searchDepth >= maxSearchDepth)
        {
            _logger.LogWarning("兼容性适配: 父目录查找达到最大深度限制 {MaxDepth}，停止查找", maxSearchDepth);
        }
        
        _logger.LogDebug("兼容性适配: 未找到任何父目录映射: {QueryPath}", normalizedPath);
        return null;
    }

    /// <summary>
    /// 检查指定路径是否为某个已索引路径的子目录 - 兼容原有接口
    /// </summary>
    public bool IsSubDirectoryOfIndexed(string queryPath, string indexedPath)
    {
        var normalizedQuery = queryPath.NormalizePath();
        var normalizedIndexed = indexedPath.NormalizePath();
        
        // 确保索引路径以路径分隔符结尾，避免误匹配
        if (!normalizedIndexed.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            normalizedIndexed += Path.DirectorySeparatorChar;
        }
        
        return normalizedQuery.StartsWith(normalizedIndexed, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 根据集合名称获取映射 - 兼容原有接口
    /// </summary>
    public CodebaseMcpServer.Services.Domain.CodebaseMapping? GetMappingByCollection(string collectionName)
    {
        try
        {
            var task = _indexLibraryService.GetByCollectionNameAsync(collectionName);
            task.Wait();
            
            var library = task.Result;
            if (library == null)
                return null;

            return ConvertToLegacyMapping(library);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "兼容性适配: 根据集合名称获取映射失败 {CollectionName}", collectionName);
            return null;
        }
    }

    /// <summary>
    /// 更新映射信息 - 兼容原有接口
    /// </summary>
    public async Task<bool> UpdateMapping(CodebaseMcpServer.Services.Domain.CodebaseMapping updatedMapping)
    {
        try
        {
            if (!int.TryParse(updatedMapping.Id, out var libraryId))
            {
                _logger.LogWarning("兼容性适配: 无效的库ID {Id}", updatedMapping.Id);
                return false;
            }

            var library = await _indexLibraryService.GetByIdAsync(libraryId);
            if (library == null)
            {
                _logger.LogWarning("兼容性适配: 要更新的映射不存在: {Id}", updatedMapping.Id);
                return false;
            }

            // 更新基础信息
            library.Name = updatedMapping.FriendlyName;
            
            // 更新统计信息
            var stats = library.StatisticsObject;
            stats.TotalFiles = updatedMapping.Statistics.TotalFiles;
            stats.IndexedSnippets = updatedMapping.Statistics.IndexedSnippets;
            
            if (DateTime.TryParse(updatedMapping.Statistics.LastIndexingDuration?.TrimEnd('s'), out var duration))
            {
                stats.LastIndexingDuration = duration.Second;
            }

            await _indexLibraryService.UpdateAsync(library);
            
            _logger.LogInformation("兼容性适配: 更新代码库映射: {Path} -> {Collection}", 
                updatedMapping.CodebasePath, updatedMapping.CollectionName);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "兼容性适配: 更新映射失败 {Id}", updatedMapping.Id);
            return false;
        }
    }

    /// <summary>
    /// 删除映射 - 兼容原有接口
    /// </summary>
    public async Task<bool> RemoveMapping(string id)
    {
        try
        {
            if (!int.TryParse(id, out var libraryId))
            {
                _logger.LogWarning("兼容性适配: 无效的库ID {Id}", id);
                return false;
            }

            var result = await _indexLibraryService.DeleteAsync(libraryId);
            
            if (result)
            {
                _logger.LogInformation("兼容性适配: 删除代码库映射: {Id}", id);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "兼容性适配: 删除映射失败 {Id}", id);
            return false;
        }
    }

    /// <summary>
    /// 根据路径删除映射 - 兼容原有接口
    /// </summary>
    public async Task<bool> RemoveMappingByPath(string codebasePath)
    {
        try
        {
            var mapping = GetMappingByPath(codebasePath);
            if (mapping == null)
            {
                _logger.LogWarning("兼容性适配: 要删除的映射不存在: {Path}", codebasePath);
                return false;
            }
            
            return await RemoveMapping(mapping.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "兼容性适配: 根据路径删除映射失败 {Path}", codebasePath);
            return false;
        }
    }

    /// <summary>
    /// 获取所有映射 - 兼容原有接口
    /// </summary>
    public List<CodebaseMcpServer.Services.Domain.CodebaseMapping> GetAllMappings()
    {
        try
        {
            var task = _indexLibraryService.GetLegacyMappingsAsync();
            task.Wait();
            return task.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "兼容性适配: 获取所有映射失败");
            return new List<CodebaseMcpServer.Services.Domain.CodebaseMapping>();
        }
    }

    /// <summary>
    /// 获取需要监控的映射 - 兼容原有接口
    /// </summary>
    public List<CodebaseMcpServer.Services.Domain.CodebaseMapping> GetMonitoredMappings()
    {
        try
        {
            var task = _indexLibraryService.GetLibrariesForMonitoringAsync();
            task.Wait();
            
            return task.Result.Select(ConvertToLegacyMapping).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "兼容性适配: 获取监控映射失败");
            return new List<CodebaseMcpServer.Services.Domain.CodebaseMapping>();
        }
    }

    /// <summary>
    /// 获取完整配置 - 兼容原有接口
    /// </summary>
    public async Task<CodebaseMcpServer.Models.IndexConfiguration> GetConfiguration()
    {
        try
        {
            var libraries = await _indexLibraryService.GetAllAsync();
            var domainMappings = libraries.Select(ConvertToLegacyMapping).ToList();
            
            var modelMappings = domainMappings.Select(m => new CodebaseMcpServer.Models.CodebaseMapping
            {
                Id = m.Id,
                FriendlyName = m.FriendlyName,
                CodebasePath = m.CodebasePath,
                NormalizedPath = m.NormalizedPath,
                CollectionName = m.CollectionName,
                IndexingStatus = m.IndexingStatus,
                IsMonitoring = m.IsMonitoring,
                CreatedAt = m.CreatedAt,
                LastIndexed = m.LastIndexed,
                Statistics = new CodebaseMcpServer.Models.IndexStatistics
                {
                    TotalFiles = m.Statistics.TotalFiles,
                    IndexedSnippets = m.Statistics.IndexedSnippets,
                    LastIndexingDuration = m.Statistics.LastIndexingDuration,
                    LastUpdateTime = m.Statistics.LastUpdateTime
                }
            }).ToList();

            return new CodebaseMcpServer.Models.IndexConfiguration
            {
                Version = "2.0", // 新版本
                LastUpdated = DateTime.UtcNow,
                CodebaseMappings = modelMappings,
                GlobalSettings = new CodebaseMcpServer.Models.GlobalSettings()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "兼容性适配: 获取配置失败");
            return new CodebaseMcpServer.Models.IndexConfiguration
            {
                Version = "2.0",
                LastUpdated = DateTime.UtcNow,
                CodebaseMappings = new List<CodebaseMcpServer.Models.CodebaseMapping>(),
                GlobalSettings = new CodebaseMcpServer.Models.GlobalSettings()
            };
        }
    }

    /// <summary>
    /// 更新映射统计信息 - 兼容原有接口
    /// </summary>
    public async Task<bool> UpdateMappingStatistics(string id, Action<CodebaseMcpServer.Services.Domain.IndexStatistics> updateAction)
    {
        try
        {
            if (!int.TryParse(id, out var libraryId))
            {
                return false;
            }

            var library = await _indexLibraryService.GetByIdAsync(libraryId);
            if (library == null)
            {
                return false;
            }

            // 创建临时的IndexStatistics对象用于更新
            var tempStats = new CodebaseMcpServer.Services.Domain.IndexStatistics
            {
                TotalFiles = library.TotalFiles,
                IndexedSnippets = library.IndexedSnippets,
                LastIndexingDuration = $"{library.StatisticsObject.LastIndexingDuration}s",
                LastUpdateTime = library.UpdatedAt
            };

            updateAction(tempStats);

            // 将更新应用回library
            var stats = library.StatisticsObject;
            stats.TotalFiles = tempStats.TotalFiles;
            stats.IndexedSnippets = tempStats.IndexedSnippets;
            
            if (double.TryParse(tempStats.LastIndexingDuration?.TrimEnd('s'), out var duration))
            {
                stats.LastIndexingDuration = duration;
            }

            library.TotalFiles = tempStats.TotalFiles;
            library.IndexedSnippets = tempStats.IndexedSnippets;

            return await _indexLibraryService.UpdateAsync(library);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "兼容性适配: 更新统计信息失败 {Id}", id);
            return false;
        }
    }

    private CodebaseMcpServer.Services.Domain.CodebaseMapping ConvertToLegacyMapping(CodebaseMcpServer.Models.Domain.IndexLibrary library)
    {
        var stats = library.StatisticsObject;
        
        return new CodebaseMcpServer.Services.Domain.CodebaseMapping
        {
            Id = library.Id.ToString(),
            FriendlyName = library.Name,
            CodebasePath = library.CodebasePath,
            NormalizedPath = library.CodebasePath.NormalizePath(),
            CollectionName = library.CollectionName,
            IndexingStatus = library.Status.ToString().ToLower(),
            IsMonitoring = library.WatchConfigObject.IsEnabled,
            CreatedAt = library.CreatedAt,
            LastIndexed = library.LastIndexedAt,
            Statistics = new CodebaseMcpServer.Services.Domain.IndexStatistics
            {
                TotalFiles = library.TotalFiles,
                IndexedSnippets = library.IndexedSnippets,
                LastIndexingDuration = $"{stats.LastIndexingDuration}s",
                LastUpdateTime = library.UpdatedAt
            }
        };
    }
}