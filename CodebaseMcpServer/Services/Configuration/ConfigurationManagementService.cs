using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Domain;
using CodebaseMcpServer.Services.Analysis;
using System.Text.Json;

namespace CodebaseMcpServer.Services.Configuration;

/// <summary>
/// 配置管理服务 - 统一管理索引库的配置、预设和验证
/// </summary>
public class ConfigurationManagementService : IConfigurationManagementService
{
    private readonly IConfigurationPresetService _presetService;
    private readonly IConfigurationValidationService _validationService;
    private readonly IIndexLibraryService _indexLibraryService;
    private readonly ILogger<ConfigurationManagementService> _logger;

    public ConfigurationManagementService(
        IConfigurationPresetService presetService,
        IConfigurationValidationService validationService,
        IIndexLibraryService indexLibraryService,
        ILogger<ConfigurationManagementService> logger)
    {
        _presetService = presetService;
        _validationService = validationService;
        _indexLibraryService = indexLibraryService;
        _logger = logger;
    }

    /// <summary>
    /// 应用预设配置到索引库
    /// </summary>
    public async Task<ConfigurationApplyResult> ApplyPresetToLibraryAsync(int libraryId, string presetId, bool validateOnly = false)
    {
        try
        {
            _logger.LogInformation("应用预设配置: LibraryId={LibraryId}, PresetId={PresetId}", libraryId, presetId);

            // 1. 获取索引库
            var library = await _indexLibraryService.GetByIdAsync(libraryId);
            if (library == null)
            {
                return ConfigurationApplyResult.CreateFailed($"索引库不存在: {libraryId}");
            }

            // 2. 获取预设
            var preset = await _presetService.GetPresetByIdAsync(presetId);
            if (preset == null)
            {
                return ConfigurationApplyResult.CreateFailed($"预设不存在: {presetId}");
            }

            // 3. 验证预设
            var presetValidation = _presetService.ValidatePreset(preset);
            if (!presetValidation.IsValid)
            {
                return ConfigurationApplyResult.CreateFailed($"预设验证失败: {string.Join(", ", presetValidation.Errors)}");
            }

            // 4. 验证监控配置
            var watchValidation = _validationService.ValidateWatchConfiguration(preset.WatchConfiguration);
            if (!watchValidation.IsValid)
            {
                return ConfigurationApplyResult.CreateFailed($"监控配置验证失败: {string.Join(", ", watchValidation.Errors)}");
            }

            // 5. 验证元数据
            var metadataValidation = _validationService.ValidateMetadata(preset.Metadata);
            if (!metadataValidation.IsValid)
            {
                return ConfigurationApplyResult.CreateFailed($"元数据验证失败: {string.Join(", ", metadataValidation.Errors)}");
            }

            // 6. 如果只是验证，返回成功
            if (validateOnly)
            {
                return ConfigurationApplyResult.CreateSuccess("预设验证通过", null);
            }

            // 7. 备份当前配置
            var backup = new ConfigurationBackup
            {
                LibraryId = libraryId,
                BackupDate = DateTime.UtcNow,
                WatchConfig = library.WatchConfigObject,
                Metadata = library.MetadataObject,
                Comment = $"应用预设前的备份: {presetId}"
            };

            // 8. 应用新配置
            var updateWatchRequest = new UpdateWatchConfigurationRequest
            {
                FilePatterns = preset.WatchConfiguration.FilePatterns.ToArray(),
                ExcludePatterns = preset.WatchConfiguration.ExcludePatterns.ToArray(),
                IncludeSubdirectories = preset.WatchConfiguration.IncludeSubdirectories,
                IsEnabled = preset.WatchConfiguration.IsEnabled,
                MaxFileSize = preset.WatchConfiguration.MaxFileSize,
                CustomFilters = preset.WatchConfiguration.CustomFilters.Select(f => new CustomFilterRequest
                {
                    Name = f.Name,
                    Pattern = f.Pattern,
                    Enabled = f.Enabled
                }).ToArray()
            };

            var watchUpdateResult = await _indexLibraryService.UpdateWatchConfigurationAsync(libraryId, updateWatchRequest);
            if (!watchUpdateResult)
            {
                return ConfigurationApplyResult.CreateFailed("更新监控配置失败");
            }

            var updateMetadataRequest = new UpdateMetadataRequest
            {
                ProjectType = preset.Metadata.ProjectType,
                Framework = preset.Metadata.Framework,
                Team = preset.Metadata.Team,
                Priority = preset.Metadata.Priority,
                Tags = preset.Metadata.Tags.ToArray(),
                CustomSettings = preset.Metadata.CustomSettings
            };

            var metadataUpdateResult = await _indexLibraryService.UpdateMetadataAsync(libraryId, updateMetadataRequest);
            if (!metadataUpdateResult)
            {
                return ConfigurationApplyResult.CreateFailed("更新元数据失败");
            }

            _logger.LogInformation("预设配置应用成功: LibraryId={LibraryId}, PresetId={PresetId}", libraryId, presetId);

            return ConfigurationApplyResult.CreateSuccess("预设配置应用成功", backup);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "应用预设配置失败: LibraryId={LibraryId}, PresetId={PresetId}", libraryId, presetId);
            return ConfigurationApplyResult.CreateFailed($"应用失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 根据项目路径智能推荐配置
    /// </summary>
    public async Task<ConfigurationRecommendation> GetSmartRecommendationAsync(string projectPath, string? currentProjectType = null)
    {
        try
        {
            _logger.LogInformation("获取智能配置推荐: {ProjectPath}", projectPath);

            var recommendation = new ConfigurationRecommendation
            {
                ProjectPath = projectPath,
                GeneratedAt = DateTime.UtcNow
            };

            // 1. 如果没有指定项目类型，尝试检测
            if (string.IsNullOrEmpty(currentProjectType))
            {
                // 这里应该调用项目检测服务，暂时使用默认逻辑
                currentProjectType = DetectProjectTypeFromPath(projectPath);
            }

            recommendation.DetectedProjectType = currentProjectType;

            // 2. 获取推荐预设
            var recommendedPresets = await _presetService.GetRecommendedPresetsAsync(currentProjectType);
            recommendation.RecommendedPresets = recommendedPresets.Take(3).ToList(); // 只取前3个

            // 3. 分析项目结构，生成自定义建议
            var structureAnalysis = await AnalyzeProjectStructureAsync(projectPath);
            recommendation.StructureAnalysis = structureAnalysis;

            // 4. 生成配置建议
            if (recommendedPresets.Any())
            {
                var topPreset = recommendedPresets.First();
                var suggestions = _validationService.GetConfigurationSuggestions(topPreset.WatchConfiguration, currentProjectType);
                recommendation.ConfigurationSuggestions = suggestions;
            }

            // 5. 生成性能建议
            recommendation.PerformanceRecommendations = GetPerformanceRecommendations(structureAnalysis);

            _logger.LogInformation("智能配置推荐生成完成: {ProjectType}, {PresetCount}个推荐预设", 
                currentProjectType, recommendedPresets.Count);

            return recommendation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取智能配置推荐失败: {ProjectPath}", projectPath);
            return new ConfigurationRecommendation
            {
                ProjectPath = projectPath,
                GeneratedAt = DateTime.UtcNow,
                Error = $"推荐生成失败: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 比较配置差异
    /// </summary>
    public ConfigurationDiff CompareConfigurations(WatchConfigurationDto config1, WatchConfigurationDto config2, string? label1 = null, string? label2 = null)
    {
        var diff = new ConfigurationDiff
        {
            Label1 = label1 ?? "配置1",
            Label2 = label2 ?? "配置2",
            HasDifferences = false
        };

        // 比较文件模式
        var filePatternsDiff = CompareStringLists(config1.FilePatterns, config2.FilePatterns);
        if (filePatternsDiff.HasDifferences)
        {
            diff.Differences.Add(new ConfigurationDifference
            {
                Property = "FilePatterns",
                Type = DifferenceType.Modified,
                OldValue = config1.FilePatterns,
                NewValue = config2.FilePatterns,
                Details = filePatternsDiff
            });
            diff.HasDifferences = true;
        }

        // 比较排除模式
        var excludePatternsDiff = CompareStringLists(config1.ExcludePatterns, config2.ExcludePatterns);
        if (excludePatternsDiff.HasDifferences)
        {
            diff.Differences.Add(new ConfigurationDifference
            {
                Property = "ExcludePatterns",
                Type = DifferenceType.Modified,
                OldValue = config1.ExcludePatterns,
                NewValue = config2.ExcludePatterns,
                Details = excludePatternsDiff
            });
            diff.HasDifferences = true;
        }

        // 比较其他属性
        if (config1.IsEnabled != config2.IsEnabled)
        {
            diff.Differences.Add(new ConfigurationDifference
            {
                Property = "IsEnabled",
                Type = DifferenceType.Modified,
                OldValue = config1.IsEnabled,
                NewValue = config2.IsEnabled
            });
            diff.HasDifferences = true;
        }

        if (config1.MaxFileSize != config2.MaxFileSize)
        {
            diff.Differences.Add(new ConfigurationDifference
            {
                Property = "MaxFileSize",
                Type = DifferenceType.Modified,
                OldValue = config1.MaxFileSize,
                NewValue = config2.MaxFileSize
            });
            diff.HasDifferences = true;
        }

        if (config1.IncludeSubdirectories != config2.IncludeSubdirectories)
        {
            diff.Differences.Add(new ConfigurationDifference
            {
                Property = "IncludeSubdirectories",
                Type = DifferenceType.Modified,
                OldValue = config1.IncludeSubdirectories,
                NewValue = config2.IncludeSubdirectories
            });
            diff.HasDifferences = true;
        }

        return diff;
    }

    /// <summary>
    /// 导出索引库配置
    /// </summary>
    public async Task<string> ExportLibraryConfigurationAsync(int libraryId, bool includeStatistics = false)
    {
        try
        {
            var library = await _indexLibraryService.GetByIdAsync(libraryId);
            if (library == null)
            {
                throw new ArgumentException($"索引库不存在: {libraryId}");
            }

            var export = new LibraryConfigurationExport
            {
                LibraryName = library.Name,
                ProjectType = library.MetadataObject.ProjectType,
                WatchConfiguration = library.WatchConfigObject,
                Metadata = library.MetadataObject,
                ExportDate = DateTime.UtcNow,
                ExportVersion = "1.0"
            };

            if (includeStatistics)
            {
                export.Statistics = library.StatisticsObject;
            }

            return JsonSerializer.Serialize(export, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出索引库配置失败: {LibraryId}", libraryId);
            throw;
        }
    }

    /// <summary>
    /// 导入配置到索引库
    /// </summary>
    public async Task<ConfigurationImportResult> ImportLibraryConfigurationAsync(int libraryId, string configJson, bool validateOnly = false)
    {
        try
        {
            var import = JsonSerializer.Deserialize<LibraryConfigurationExport>(configJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (import == null)
            {
                return ConfigurationImportResult.CreateFailed("配置JSON格式无效");
            }

            // 验证配置
            var watchValidation = _validationService.ValidateWatchConfiguration(import.WatchConfiguration);
            if (!watchValidation.IsValid)
            {
                return ConfigurationImportResult.CreateFailed($"监控配置验证失败: {string.Join(", ", watchValidation.Errors)}");
            }

            var metadataValidation = _validationService.ValidateMetadata(import.Metadata);
            if (!metadataValidation.IsValid)
            {
                return ConfigurationImportResult.CreateFailed($"元数据验证失败: {string.Join(", ", metadataValidation.Errors)}");
            }

            if (validateOnly)
            {
                return ConfigurationImportResult.CreateSuccess("配置验证通过");
            }

            // 应用配置
            var updateWatchRequest = new UpdateWatchConfigurationRequest
            {
                FilePatterns = import.WatchConfiguration.FilePatterns.ToArray(),
                ExcludePatterns = import.WatchConfiguration.ExcludePatterns.ToArray(),
                IncludeSubdirectories = import.WatchConfiguration.IncludeSubdirectories,
                IsEnabled = import.WatchConfiguration.IsEnabled,
                MaxFileSize = import.WatchConfiguration.MaxFileSize,
                CustomFilters = import.WatchConfiguration.CustomFilters.Select(f => new CustomFilterRequest
                {
                    Name = f.Name,
                    Pattern = f.Pattern,
                    Enabled = f.Enabled
                }).ToArray()
            };

            var watchResult = await _indexLibraryService.UpdateWatchConfigurationAsync(libraryId, updateWatchRequest);
            if (!watchResult)
            {
                return ConfigurationImportResult.CreateFailed("更新监控配置失败");
            }

            var updateMetadataRequest = new UpdateMetadataRequest
            {
                ProjectType = import.Metadata.ProjectType,
                Framework = import.Metadata.Framework,
                Team = import.Metadata.Team,
                Priority = import.Metadata.Priority,
                Tags = import.Metadata.Tags.ToArray(),
                CustomSettings = import.Metadata.CustomSettings
            };

            var metadataResult = await _indexLibraryService.UpdateMetadataAsync(libraryId, updateMetadataRequest);
            if (!metadataResult)
            {
                return ConfigurationImportResult.CreateFailed("更新元数据失败");
            }

            return ConfigurationImportResult.CreateSuccess("配置导入成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入配置失败: {LibraryId}", libraryId);
            return ConfigurationImportResult.CreateFailed($"导入失败: {ex.Message}");
        }
    }

    // 私有方法实现
    private string DetectProjectTypeFromPath(string projectPath)
    {
        try
        {
            if (Directory.GetFiles(projectPath, "*.csproj", SearchOption.TopDirectoryOnly).Any())
                return "csharp";
            if (File.Exists(Path.Combine(projectPath, "package.json")))
                return "typescript";
            if (File.Exists(Path.Combine(projectPath, "requirements.txt")))
                return "python";
            
            return "mixed";
        }
        catch
        {
            return "unknown";
        }
    }

    private async Task<ProjectStructureAnalysis> AnalyzeProjectStructureAsync(string projectPath)
    {
        var analysis = new ProjectStructureAnalysis();

        try
        {
            if (!Directory.Exists(projectPath))
            {
                analysis.Error = "项目路径不存在";
                return analysis;
            }

            // 分析文件数量和类型
            var allFiles = Directory.GetFiles(projectPath, "*", SearchOption.AllDirectories);
            analysis.TotalFiles = allFiles.Length;

            var filesByExtension = allFiles
                .GroupBy(f => Path.GetExtension(f).ToLower())
                .ToDictionary(g => g.Key, g => g.Count());

            analysis.FileTypeDistribution = filesByExtension;

            // 分析目录结构
            var directories = Directory.GetDirectories(projectPath, "*", SearchOption.AllDirectories);
            analysis.TotalDirectories = directories.Length;

            // 检测常见的目录模式
            analysis.HasTestDirectory = directories.Any(d => Path.GetFileName(d).ToLower().Contains("test"));
            analysis.HasDocumentationDirectory = directories.Any(d => Path.GetFileName(d).ToLower().Contains("doc"));
            
            // 计算项目大小
            analysis.TotalSizeBytes = allFiles.Sum(f => new FileInfo(f).Length);

            // 生成建议的排除模式
            var suggestedExcludes = new List<string>();
            if (directories.Any(d => Path.GetFileName(d) == "node_modules"))
                suggestedExcludes.Add("node_modules");
            if (directories.Any(d => Path.GetFileName(d) == "bin"))
                suggestedExcludes.Add("bin");
            if (directories.Any(d => Path.GetFileName(d) == "obj"))
                suggestedExcludes.Add("obj");

            analysis.SuggestedExcludePatterns = suggestedExcludes;
        }
        catch (Exception ex)
        {
            analysis.Error = $"分析失败: {ex.Message}";
        }

        return analysis;
    }

    private List<string> GetPerformanceRecommendations(ProjectStructureAnalysis analysis)
    {
        var recommendations = new List<string>();

        if (analysis.TotalFiles > 10000)
        {
            recommendations.Add("项目文件数量较多，建议添加更多排除模式以提高索引性能");
        }

        if (analysis.TotalSizeBytes > 1024 * 1024 * 1024) // 1GB
        {
            recommendations.Add("项目体积较大，建议设置较小的文件大小限制");
        }

        if (analysis.FileTypeDistribution.ContainsKey(".log") && analysis.FileTypeDistribution[".log"] > 100)
        {
            recommendations.Add("检测到大量日志文件，建议将*.log添加到排除模式");
        }

        return recommendations;
    }

    private ListComparisonResult CompareStringLists(List<string> list1, List<string> list2)
    {
        var result = new ListComparisonResult();
        
        result.AddedItems = list2.Except(list1).ToList();
        result.RemovedItems = list1.Except(list2).ToList();
        result.HasDifferences = result.AddedItems.Any() || result.RemovedItems.Any();

        return result;
    }
}

/// <summary>
/// 配置管理服务接口
/// </summary>
public interface IConfigurationManagementService
{
    Task<ConfigurationApplyResult> ApplyPresetToLibraryAsync(int libraryId, string presetId, bool validateOnly = false);
    Task<ConfigurationRecommendation> GetSmartRecommendationAsync(string projectPath, string? currentProjectType = null);
    ConfigurationDiff CompareConfigurations(WatchConfigurationDto config1, WatchConfigurationDto config2, string? label1 = null, string? label2 = null);
    Task<string> ExportLibraryConfigurationAsync(int libraryId, bool includeStatistics = false);
    Task<ConfigurationImportResult> ImportLibraryConfigurationAsync(int libraryId, string configJson, bool validateOnly = false);
}

// 相关数据模型
public class ConfigurationApplyResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;
    public ConfigurationBackup? Backup { get; set; }

    public static ConfigurationApplyResult CreateSuccess(string message, ConfigurationBackup? backup)
    {
        return new ConfigurationApplyResult { IsSuccess = true, Message = message, Backup = backup };
    }

    public static ConfigurationApplyResult CreateFailed(string message)
    {
        return new ConfigurationApplyResult { IsSuccess = false, Message = message };
    }
}

public class ConfigurationImportResult
{
    public bool IsSuccess { get; set; }
    public string Message { get; set; } = string.Empty;

    public static ConfigurationImportResult CreateSuccess(string message)
    {
        return new ConfigurationImportResult { IsSuccess = true, Message = message };
    }

    public static ConfigurationImportResult CreateFailed(string message)
    {
        return new ConfigurationImportResult { IsSuccess = false, Message = message };
    }
}

public class ConfigurationBackup
{
    public int LibraryId { get; set; }
    public DateTime BackupDate { get; set; }
    public WatchConfigurationDto WatchConfig { get; set; } = new();
    public MetadataDto Metadata { get; set; } = new();
    public string Comment { get; set; } = string.Empty;
}

public class ConfigurationRecommendation
{
    public string ProjectPath { get; set; } = string.Empty;
    public string DetectedProjectType { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public List<ConfigurationPreset> RecommendedPresets { get; set; } = new();
    public ProjectStructureAnalysis StructureAnalysis { get; set; } = new();
    public List<ConfigurationSuggestion> ConfigurationSuggestions { get; set; } = new();
    public List<string> PerformanceRecommendations { get; set; } = new();
    public string? Error { get; set; }
}

public class ProjectStructureAnalysis
{
    public int TotalFiles { get; set; }
    public int TotalDirectories { get; set; }
    public long TotalSizeBytes { get; set; }
    public Dictionary<string, int> FileTypeDistribution { get; set; } = new();
    public bool HasTestDirectory { get; set; }
    public bool HasDocumentationDirectory { get; set; }
    public List<string> SuggestedExcludePatterns { get; set; } = new();
    public string? Error { get; set; }
}

public class ConfigurationDiff
{
    public string Label1 { get; set; } = string.Empty;
    public string Label2 { get; set; } = string.Empty;
    public bool HasDifferences { get; set; }
    public List<ConfigurationDifference> Differences { get; set; } = new();
}

public class ConfigurationDifference
{
    public string Property { get; set; } = string.Empty;
    public DifferenceType Type { get; set; }
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
    public ListComparisonResult? Details { get; set; }
}

public class ListComparisonResult
{
    public bool HasDifferences { get; set; }
    public List<string> AddedItems { get; set; } = new();
    public List<string> RemovedItems { get; set; } = new();
}

public enum DifferenceType
{
    Added,
    Removed,
    Modified
}

public class LibraryConfigurationExport
{
    public string LibraryName { get; set; } = string.Empty;
    public string ProjectType { get; set; } = string.Empty;
    public WatchConfigurationDto WatchConfiguration { get; set; } = new();
    public MetadataDto Metadata { get; set; } = new();
    public StatisticsDto? Statistics { get; set; }
    public DateTime ExportDate { get; set; }
    public string ExportVersion { get; set; } = string.Empty;
}