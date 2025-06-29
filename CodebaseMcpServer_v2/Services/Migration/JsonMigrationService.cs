using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Data;
using CodebaseMcpServer.Services.Data.Repositories;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Dapper;

namespace CodebaseMcpServer.Services.Migration;

/// <summary>
/// JSON数据迁移服务 - 从现有JSON文件迁移到SQLite + JSON
/// </summary>
public class JsonMigrationService : IJsonMigrationService
{
    private readonly DatabaseContext _context;
    private readonly IIndexLibraryRepository _libraryRepository;
    private readonly ILogger<JsonMigrationService> _logger;
    private const string LEGACY_CONFIG_FILE = "codebase-indexes.json";
    private const string BACKUP_SUFFIX = ".backup";
    private string LegacyConfigFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LEGACY_CONFIG_FILE);

    public JsonMigrationService(
        DatabaseContext context, 
        IIndexLibraryRepository libraryRepository,
        ILogger<JsonMigrationService> logger)
    {
        _context = context;
        _libraryRepository = libraryRepository;
        _logger = logger;
    }

    public async Task<MigrationResult> MigrateFromLegacyAsync()
    {
        var result = new MigrationResult();
        
        try
        {
            _logger.LogInformation("开始JSON数据迁移...");
            
            // 1. 检查是否已经迁移过
            var existingLibraries = await _libraryRepository.GetAllAsync();
            if (existingLibraries.Any())
            {
                _logger.LogInformation("检测到现有数据，跳过迁移");
                result.Success = true;
                result.Message = "数据库中已存在数据，跳过迁移";
                return result;
            }
            
            // 2. 备份现有配置
            await BackupLegacyConfigAsync();
            
            // 3. 读取现有JSON配置
            var legacyConfig = await ReadLegacyConfigAsync();
            if (legacyConfig == null)
            {
                result.Success = true;
                result.Message = "未发现需要迁移的配置文件";
                return result;
            }
            
            using var transaction = await _context.BeginTransactionAsync();
            
            try
            {
                // 4. 迁移索引库配置
                foreach (var mapping in legacyConfig.CodebaseMappings)
                {
                    var library = await MigrateIndexLibraryWithJsonAsync(mapping);
                    result.MigratedLibraries.Add(library);
                }
                
                // 5. 迁移系统配置
                if (legacyConfig.GlobalSettings != null)
                {
                    await MigrateSystemConfigurationsAsync(legacyConfig.GlobalSettings);
                }
                
                await _context.CommitAsync();
                
                result.Success = true;
                result.Message = $"成功迁移 {result.MigratedLibraries.Count} 个索引库到JSON格式";
                
                _logger.LogInformation("JSON数据迁移完成: {Count} 个索引库", result.MigratedLibraries.Count);
            }
            catch (Exception ex)
            {
                await _context.RollbackAsync();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JSON数据迁移失败");
            result.Success = false;
            result.Message = $"迁移失败: {ex.Message}";
        }
        
        return result;
    }

    private async Task BackupLegacyConfigAsync()
    {
        try
        {
            var filePath = LegacyConfigFilePath;
            if (File.Exists(filePath))
            {
                var backupFileName = $"{filePath}.{DateTime.UtcNow:yyyyMMdd_HHmmss}{BACKUP_SUFFIX}";
                File.Copy(filePath, backupFileName, true);
                _logger.LogInformation("备份配置文件: {BackupFile}", backupFileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "备份配置文件失败");
        }
    }

    private async Task<LegacyConfiguration?> ReadLegacyConfigAsync()
    {
        try
        {
            var filePath = LegacyConfigFilePath;
            if (!File.Exists(filePath))
            {
                _logger.LogInformation("未找到传统配置文件: {File}", filePath);
                return null;
            }

            var jsonContent = await File.ReadAllTextAsync(filePath);
            var config = JsonSerializer.Deserialize<LegacyConfiguration>(jsonContent, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            _logger.LogInformation("读取到 {Count} 个传统配置映射", config?.CodebaseMappings?.Count ?? 0);
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取传统配置文件失败");
            return null;
        }
    }

    private async Task<IndexLibrary> MigrateIndexLibraryWithJsonAsync(LegacyCodebaseMapping mapping)
    {
        // 转换监控配置为JSON
        var watchConfig = new WatchConfigurationDto
        {
            FilePatterns = mapping.WatcherConfig?.FileExtensions ?? new List<string> { "*.cs" },
            ExcludePatterns = mapping.WatcherConfig?.ExcludeDirectories ?? new List<string> { "bin", "obj", ".git" },
            IncludeSubdirectories = mapping.WatcherConfig?.IncludeSubdirectories ?? true,
            IsEnabled = mapping.IsMonitoring,
            MaxFileSize = 10 * 1024 * 1024, // 默认10MB
            CustomFilters = new List<CustomFilterDto>()
        };
        
        // 转换统计信息为JSON
        var statistics = new StatisticsDto
        {
            IndexedSnippets = mapping.Statistics?.IndexedSnippets ?? 0,
            TotalFiles = mapping.Statistics?.TotalFiles ?? 0,
            LastIndexingDuration = ParseDuration(mapping.Statistics?.LastIndexingDuration),
            AverageFileSize = 0, // 需要重新计算
            LanguageDistribution = new Dictionary<string, int> { ["csharp"] = mapping.Statistics?.TotalFiles ?? 0 },
            IndexingHistory = new List<IndexingHistoryDto>()
        };
        
        // 如果有历史记录，添加一条
        if (mapping.LastIndexed.HasValue && statistics.LastIndexingDuration > 0)
        {
            statistics.IndexingHistory.Add(new IndexingHistoryDto
            {
                Date = mapping.LastIndexed.Value,
                Duration = statistics.LastIndexingDuration,
                FilesProcessed = statistics.TotalFiles,
                SnippetsCreated = statistics.IndexedSnippets
            });
        }
        
        // 转换元数据为JSON
        var metadata = new MetadataDto
        {
            ProjectType = DetectProjectTypeFromPath(mapping.CodebasePath),
            Framework = "unknown",
            Team = "default",
            Priority = "normal",
            Tags = new List<string> { "migrated", "legacy" },
            CustomSettings = new Dictionary<string, object>
            {
                ["originalId"] = mapping.Id,
                ["friendlyName"] = mapping.FriendlyName,
                ["migrationDate"] = DateTime.UtcNow,
                ["originalCreatedAt"] = mapping.CreatedAt
            }
        };
        
        var library = new IndexLibrary
        {
            Name = mapping.FriendlyName,
            CodebasePath = mapping.CodebasePath,
            CollectionName = mapping.CollectionName,
            Status = MapLegacyStatus(mapping.IndexingStatus),
            WatchConfig = JsonSerializer.Serialize(watchConfig),
            Statistics = JsonSerializer.Serialize(statistics),
            Metadata = JsonSerializer.Serialize(metadata),
            CreatedAt = mapping.CreatedAt,
            LastIndexedAt = mapping.LastIndexed,
            TotalFiles = mapping.Statistics?.TotalFiles ?? 0,
            IndexedSnippets = mapping.Statistics?.IndexedSnippets ?? 0,
            IsActive = true
        };
        
        library = await _libraryRepository.CreateAsync(library);
        
        _logger.LogInformation("迁移索引库: {Name} -> JSON格式", library.Name);
        
        return library;
    }

    private string DetectProjectTypeFromPath(string codebasePath)
    {
        try
        {
            if (Directory.GetFiles(codebasePath, "*.csproj", SearchOption.AllDirectories).Any() ||
                Directory.GetFiles(codebasePath, "*.sln", SearchOption.AllDirectories).Any())
                return "csharp";
            
            if (File.Exists(Path.Combine(codebasePath, "package.json")) ||
                File.Exists(Path.Combine(codebasePath, "tsconfig.json")))
                return "typescript";
                
            if (File.Exists(Path.Combine(codebasePath, "requirements.txt")) ||
                File.Exists(Path.Combine(codebasePath, "setup.py")) ||
                File.Exists(Path.Combine(codebasePath, "pyproject.toml")))
                return "python";
                
            return "mixed";
        }
        catch
        {
            return "unknown";
        }
    }

    private double ParseDuration(string? durationString)
    {
        if (string.IsNullOrEmpty(durationString))
            return 0;
            
        if (durationString.EndsWith("s") && 
            double.TryParse(durationString[..^1], out var seconds))
        {
            return seconds;
        }
        
        if (durationString.EndsWith("ms") && 
            double.TryParse(durationString[..^2], out var milliseconds))
        {
            return milliseconds / 1000.0;
        }
        
        return 0;
    }

    private IndexLibraryStatus MapLegacyStatus(string? legacyStatus)
    {
        return legacyStatus?.ToLower() switch
        {
            "completed" => IndexLibraryStatus.Completed,
            "failed" => IndexLibraryStatus.Failed,
            "indexing" => IndexLibraryStatus.Indexing,
            "cancelled" => IndexLibraryStatus.Cancelled,
            _ => IndexLibraryStatus.Pending
        };
    }

    private async Task MigrateSystemConfigurationsAsync(Dictionary<string, object> globalSettings)
    {
        try
        {
            foreach (var setting in globalSettings)
            {
                var sql = @"
                    INSERT INTO SystemConfigurations (ConfigKey, ConfigValue, ConfigType, Description, IsEditable)
                    VALUES (@ConfigKey, @ConfigValue, @ConfigType, @Description, @IsEditable)";
                
                var configValue = JsonSerializer.Serialize(setting.Value);
                var configType = setting.Value?.GetType().Name.ToLower() ?? "string";
                
                await _context.Connection.ExecuteAsync(sql, new
                {
                    ConfigKey = setting.Key,
                    ConfigValue = configValue,
                    ConfigType = configType,
                    Description = $"从传统配置迁移的设置: {setting.Key}",
                    IsEditable = true
                });
            }
            
            _logger.LogInformation("迁移系统配置: {Count} 个设置", globalSettings.Count);
        }
        catch (Exception ex) // Keep ex for logging
        {
            _logger.LogError(ex, "迁移系统配置失败");
        }
    }
}

/// <summary>
/// 迁移结果
/// </summary>
public class MigrationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<IndexLibrary> MigratedLibraries { get; set; } = new();
}

/// <summary>
/// 迁移服务接口
/// </summary>
public interface IJsonMigrationService
{
    Task<MigrationResult> MigrateFromLegacyAsync();
}

// 传统配置数据结构
public class LegacyConfiguration
{
    public List<LegacyCodebaseMapping> CodebaseMappings { get; set; } = new();
    public Dictionary<string, object>? GlobalSettings { get; set; }
}

public class LegacyCodebaseMapping
{
    public string Id { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string CodebasePath { get; set; } = string.Empty;
    public string CollectionName { get; set; } = string.Empty;
    public string IndexingStatus { get; set; } = string.Empty;
    public bool IsMonitoring { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastIndexed { get; set; }
    public LegacyWatcherConfig? WatcherConfig { get; set; }
    public LegacyStatistics? Statistics { get; set; }
    public List<LegacyFileDetail>? FileIndexDetails { get; set; }
}

public class LegacyWatcherConfig
{
    public List<string> FileExtensions { get; set; } = new();
    public List<string> ExcludeDirectories { get; set; } = new();
    public bool IncludeSubdirectories { get; set; } = true;
}

public class LegacyStatistics
{
    public int TotalFiles { get; set; }
    public int IndexedSnippets { get; set; }
    public string? LastIndexingDuration { get; set; }
    public DateTime? LastUpdateTime { get; set; }
}

public class LegacyFileDetail
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime LastIndexed { get; set; }
    public string FileHash { get; set; } = string.Empty;
    public int SnippetCount { get; set; }
}