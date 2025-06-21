using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Analysis;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodebaseMcpServer.Services.Configuration;

/// <summary>
/// 配置验证服务 - 验证JSON配置的格式、安全性和有效性
/// </summary>
public class ConfigurationValidationService : IConfigurationValidationService
{
    private readonly ILogger<ConfigurationValidationService> _logger;
    
    // 支持的文件扩展名模式
    private static readonly HashSet<string> AllowedFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".csx", ".cshtml", ".razor", ".vb", ".fs",  // .NET
        ".ts", ".tsx", ".js", ".jsx", ".mjs", ".vue", ".svelte",  // JavaScript/TypeScript
        ".py", ".pyi", ".pyx", ".ipynb",  // Python
        ".java", ".kt", ".scala", ".groovy",  // JVM
        ".cpp", ".c", ".h", ".hpp", ".cc", ".cxx", ".hh", ".hxx",  // C/C++
        ".go",  // Go
        ".rs",  // Rust
        ".php", ".rb", ".swift", ".dart", ".lua",  // Other languages
        ".html", ".htm", ".css", ".scss", ".sass", ".less",  // Web
        ".xml", ".json", ".yaml", ".yml", ".toml", ".ini",  // Config
        ".sql", ".md", ".txt", ".log"  // Data/Doc
    };

    // 危险的排除模式（可能导致安全问题）
    private static readonly HashSet<string> DangerousPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "..", "~", "$", "//", "\\\\", "|", "&", ";", "`"
    };

    public ConfigurationValidationService(ILogger<ConfigurationValidationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 验证监控配置
    /// </summary>
    public ValidationResult ValidateWatchConfiguration(WatchConfigurationDto config)
    {
        var errors = new List<string>();

        try
        {
            // 1. 验证文件模式
            var filePatternResult = ValidateFilePatterns(config.FilePatterns);
            errors.AddRange(filePatternResult.Errors);

            // 2. 验证排除模式
            var excludePatternResult = ValidateExcludePatterns(config.ExcludePatterns);
            errors.AddRange(excludePatternResult.Errors);

            // 3. 验证文件大小限制
            if (config.MaxFileSize <= 0)
            {
                errors.Add("文件大小限制必须大于0");
            }
            else if (config.MaxFileSize > 100 * 1024 * 1024) // 100MB
            {
                errors.Add("文件大小限制不能超过100MB");
            }

            // 4. 验证自定义过滤器
            var filterResult = ValidateCustomFilters(config.CustomFilters);
            errors.AddRange(filterResult.Errors);

            // 5. 检查逻辑冲突
            var conflictResult = CheckPatternConflicts(config.FilePatterns, config.ExcludePatterns);
            errors.AddRange(conflictResult.Errors);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证监控配置时发生异常");
            errors.Add($"验证过程异常: {ex.Message}");
        }

        return new ValidationResult(errors);
    }

    /// <summary>
    /// 验证元数据配置
    /// </summary>
    public ValidationResult ValidateMetadata(MetadataDto metadata)
    {
        var errors = new List<string>();

        try
        {
            // 1. 验证项目类型
            if (string.IsNullOrWhiteSpace(metadata.ProjectType))
            {
                errors.Add("项目类型不能为空");
            }
            else if (!IsValidProjectType(metadata.ProjectType))
            {
                errors.Add($"不支持的项目类型: {metadata.ProjectType}");
            }

            // 2. 验证优先级
            if (!string.IsNullOrEmpty(metadata.Priority) && !IsValidPriority(metadata.Priority))
            {
                errors.Add($"无效的优先级: {metadata.Priority}");
            }

            // 3. 验证标签
            var tagResult = ValidateTags(metadata.Tags);
            errors.AddRange(tagResult.Errors);

            // 4. 验证自定义设置
            var settingsResult = ValidateCustomSettings(metadata.CustomSettings);
            errors.AddRange(settingsResult.Errors);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证元数据配置时发生异常");
            errors.Add($"验证过程异常: {ex.Message}");
        }

        return new ValidationResult(errors);
    }

    /// <summary>
    /// 验证JSON配置字符串
    /// </summary>
    public ValidationResult ValidateJsonString(string jsonString, string configType)
    {
        var errors = new List<string>();

        try
        {
            if (string.IsNullOrWhiteSpace(jsonString))
            {
                errors.Add("JSON配置不能为空");
                return new ValidationResult(errors);
            }

            // 1. 验证JSON格式
            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(jsonString);
            }
            catch (JsonException ex)
            {
                errors.Add($"JSON格式错误: {ex.Message}");
                return new ValidationResult(errors);
            }

            // 2. 验证JSON大小
            if (jsonString.Length > 1024 * 1024) // 1MB
            {
                errors.Add("JSON配置大小不能超过1MB");
            }

            // 3. 验证JSON深度
            var depth = CalculateJsonDepth(document.RootElement);
            if (depth > 10)
            {
                errors.Add("JSON配置嵌套深度不能超过10层");
            }

            // 4. 根据配置类型进行特定验证
            switch (configType.ToLower())
            {
                case "watchconfig":
                    var watchConfig = JsonSerializer.Deserialize<WatchConfigurationDto>(jsonString);
                    if (watchConfig != null)
                    {
                        var watchResult = ValidateWatchConfiguration(watchConfig);
                        errors.AddRange(watchResult.Errors);
                    }
                    break;

                case "metadata":
                    var metadata = JsonSerializer.Deserialize<MetadataDto>(jsonString);
                    if (metadata != null)
                    {
                        var metaResult = ValidateMetadata(metadata);
                        errors.AddRange(metaResult.Errors);
                    }
                    break;
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证JSON字符串时发生异常");
            errors.Add($"验证过程异常: {ex.Message}");
        }

        return new ValidationResult(errors);
    }

    /// <summary>
    /// 清理和规范化配置
    /// </summary>
    public CleanupResult CleanupConfiguration(WatchConfigurationDto config)
    {
        var result = new CleanupResult { OriginalConfig = config };
        var cleanedConfig = new WatchConfigurationDto
        {
            FilePatterns = new List<string>(),
            ExcludePatterns = new List<string>(),
            IncludeSubdirectories = config.IncludeSubdirectories,
            IsEnabled = config.IsEnabled,
            MaxFileSize = Math.Min(config.MaxFileSize, 100 * 1024 * 1024), // 限制最大100MB
            CustomFilters = new List<CustomFilterDto>()
        };

        // 清理文件模式
        foreach (var pattern in config.FilePatterns)
        {
            var cleanedPattern = CleanFilePattern(pattern);
            if (!string.IsNullOrEmpty(cleanedPattern))
            {
                cleanedConfig.FilePatterns.Add(cleanedPattern);
            }
            else
            {
                result.RemovedItems.Add($"无效的文件模式: {pattern}");
            }
        }

        // 清理排除模式
        foreach (var pattern in config.ExcludePatterns)
        {
            var cleanedPattern = CleanExcludePattern(pattern);
            if (!string.IsNullOrEmpty(cleanedPattern))
            {
                cleanedConfig.ExcludePatterns.Add(cleanedPattern);
            }
            else
            {
                result.RemovedItems.Add($"无效的排除模式: {pattern}");
            }
        }

        // 清理自定义过滤器
        foreach (var filter in config.CustomFilters)
        {
            if (IsValidFilterName(filter.Name) && IsValidPattern(filter.Pattern))
            {
                cleanedConfig.CustomFilters.Add(new CustomFilterDto
                {
                    Name = filter.Name.Trim(),
                    Pattern = filter.Pattern.Trim(),
                    Enabled = filter.Enabled
                });
            }
            else
            {
                result.RemovedItems.Add($"无效的自定义过滤器: {filter.Name}");
            }
        }

        // 去重
        cleanedConfig.FilePatterns = cleanedConfig.FilePatterns.Distinct().ToList();
        cleanedConfig.ExcludePatterns = cleanedConfig.ExcludePatterns.Distinct().ToList();

        result.CleanedConfig = cleanedConfig;
        result.HasChanges = !AreConfigurationsEqual(config, cleanedConfig);

        return result;
    }

    /// <summary>
    /// 获取配置建议
    /// </summary>
    public List<ConfigurationSuggestion> GetConfigurationSuggestions(WatchConfigurationDto config, string projectType)
    {
        var suggestions = new List<ConfigurationSuggestion>();

        try
        {
            // 1. 文件模式建议
            if (!config.FilePatterns.Any())
            {
                suggestions.Add(new ConfigurationSuggestion
                {
                    Type = SuggestionType.Warning,
                    Message = "没有配置文件模式，建议添加至少一个文件类型",
                    SuggestedAction = "添加文件模式",
                    SuggestedValue = GetDefaultFilePatterns(projectType)
                });
            }

            // 2. 常见排除建议
            var commonExcludes = GetCommonExcludePatterns(projectType);
            var missingExcludes = commonExcludes.Except(config.ExcludePatterns, StringComparer.OrdinalIgnoreCase).ToList();
            if (missingExcludes.Any())
            {
                suggestions.Add(new ConfigurationSuggestion
                {
                    Type = SuggestionType.Info,
                    Message = $"建议添加常见的排除模式: {string.Join(", ", missingExcludes)}",
                    SuggestedAction = "添加排除模式",
                    SuggestedValue = missingExcludes
                });
            }

            // 3. 文件大小建议
            if (config.MaxFileSize > 50 * 1024 * 1024) // 50MB
            {
                suggestions.Add(new ConfigurationSuggestion
                {
                    Type = SuggestionType.Warning,
                    Message = "文件大小限制过大，可能影响索引性能",
                    SuggestedAction = "调整文件大小限制",
                    SuggestedValue = 10 * 1024 * 1024 // 10MB
                });
            }

            // 4. 性能建议
            if (config.FilePatterns.Count > 20)
            {
                suggestions.Add(new ConfigurationSuggestion
                {
                    Type = SuggestionType.Performance,
                    Message = "文件模式过多可能影响性能，建议合并相似模式",
                    SuggestedAction = "优化文件模式",
                    SuggestedValue = null
                });
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成配置建议时发生异常");
        }

        return suggestions;
    }

    // 私有方法实现
    private ValidationResult ValidateFilePatterns(List<string> patterns)
    {
        var errors = new List<string>();

        if (!patterns.Any())
        {
            errors.Add("至少需要一个文件模式");
            return new ValidationResult(errors);
        }

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                errors.Add("文件模式不能为空");
                continue;
            }

            if (!IsValidFilePattern(pattern))
            {
                errors.Add($"无效的文件模式: {pattern}");
            }

            if (ContainsDangerousPattern(pattern))
            {
                errors.Add($"文件模式包含危险字符: {pattern}");
            }
        }

        return new ValidationResult(errors);
    }

    private ValidationResult ValidateExcludePatterns(List<string> patterns)
    {
        var errors = new List<string>();

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue; // 排除模式可以为空
            }

            if (ContainsDangerousPattern(pattern))
            {
                errors.Add($"排除模式包含危险字符: {pattern}");
            }

            if (pattern.Length > 500)
            {
                errors.Add($"排除模式过长: {pattern}");
            }
        }

        return new ValidationResult(errors);
    }

    private ValidationResult ValidateCustomFilters(List<CustomFilterDto> filters)
    {
        var errors = new List<string>();

        foreach (var filter in filters)
        {
            if (string.IsNullOrWhiteSpace(filter.Name))
            {
                errors.Add("自定义过滤器名称不能为空");
                continue;
            }

            if (!IsValidFilterName(filter.Name))
            {
                errors.Add($"无效的过滤器名称: {filter.Name}");
            }

            if (!IsValidPattern(filter.Pattern))
            {
                errors.Add($"无效的过滤器模式: {filter.Pattern}");
            }
        }

        return new ValidationResult(errors);
    }

    private ValidationResult CheckPatternConflicts(List<string> filePatterns, List<string> excludePatterns)
    {
        var errors = new List<string>();

        // 检查是否有文件模式被排除模式完全覆盖
        foreach (var filePattern in filePatterns)
        {
            var extension = GetPatternExtension(filePattern);
            if (!string.IsNullOrEmpty(extension))
            {
                foreach (var excludePattern in excludePatterns)
                {
                    if (excludePattern.Contains(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        errors.Add($"文件模式 {filePattern} 可能被排除模式 {excludePattern} 覆盖");
                    }
                }
            }
        }

        return new ValidationResult(errors);
    }

    private bool IsValidFilePattern(string pattern)
    {
        try
        {
            // 检查是否是有效的文件扩展名模式
            if (pattern.StartsWith("*."))
            {
                var extension = pattern[1..]; // 移除第一个*
                return AllowedFileExtensions.Contains(extension);
            }

            // 检查是否是有效的通配符模式
            return Regex.IsMatch(pattern, @"^[\w\*\?\./-]+$");
        }
        catch
        {
            return false;
        }
    }

    private bool ContainsDangerousPattern(string pattern)
    {
        return DangerousPatterns.Any(dangerous => pattern.Contains(dangerous));
    }

    private bool IsValidProjectType(string projectType)
    {
        return Enum.TryParse<ProjectTypeDetector.ProjectType>(projectType, true, out _);
    }

    private bool IsValidPriority(string priority)
    {
        var validPriorities = new[] { "low", "normal", "high", "critical" };
        return validPriorities.Contains(priority.ToLower());
    }

    private ValidationResult ValidateTags(List<string> tags)
    {
        var errors = new List<string>();

        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                errors.Add("标签不能为空");
                continue;
            }

            if (tag.Length > 50)
            {
                errors.Add($"标签过长: {tag}");
            }

            if (!Regex.IsMatch(tag, @"^[\w\-\.]+$"))
            {
                errors.Add($"标签包含无效字符: {tag}");
            }
        }

        return new ValidationResult(errors);
    }

    private ValidationResult ValidateCustomSettings(Dictionary<string, object> settings)
    {
        var errors = new List<string>();

        if (settings.Count > 50)
        {
            errors.Add("自定义设置数量不能超过50个");
        }

        foreach (var setting in settings)
        {
            if (string.IsNullOrWhiteSpace(setting.Key))
            {
                errors.Add("设置键不能为空");
                continue;
            }

            if (setting.Key.Length > 100)
            {
                errors.Add($"设置键过长: {setting.Key}");
            }

            // 检查值的大小
            var serialized = JsonSerializer.Serialize(setting.Value);
            if (serialized.Length > 10000)
            {
                errors.Add($"设置值过大: {setting.Key}");
            }
        }

        return new ValidationResult(errors);
    }

    private int CalculateJsonDepth(JsonElement element, int currentDepth = 0)
    {
        var maxDepth = currentDepth;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var depth = CalculateJsonDepth(property.Value, currentDepth + 1);
                    maxDepth = Math.Max(maxDepth, depth);
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var depth = CalculateJsonDepth(item, currentDepth + 1);
                    maxDepth = Math.Max(maxDepth, depth);
                }
                break;
        }

        return maxDepth;
    }

    private string CleanFilePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return string.Empty;

        var cleaned = pattern.Trim();
        
        if (ContainsDangerousPattern(cleaned) || !IsValidFilePattern(cleaned))
            return string.Empty;

        return cleaned;
    }

    private string CleanExcludePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return string.Empty;

        var cleaned = pattern.Trim();
        
        if (ContainsDangerousPattern(cleaned))
            return string.Empty;

        return cleaned;
    }

    private bool IsValidFilterName(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && 
               name.Length <= 50 && 
               Regex.IsMatch(name, @"^[\w\-\s]+$");
    }

    private bool IsValidPattern(string pattern)
    {
        return !string.IsNullOrWhiteSpace(pattern) && 
               pattern.Length <= 200 && 
               !ContainsDangerousPattern(pattern);
    }

    private bool AreConfigurationsEqual(WatchConfigurationDto config1, WatchConfigurationDto config2)
    {
        // 简化的相等性检查
        return config1.FilePatterns.SequenceEqual(config2.FilePatterns) &&
               config1.ExcludePatterns.SequenceEqual(config2.ExcludePatterns) &&
               config1.MaxFileSize == config2.MaxFileSize &&
               config1.IsEnabled == config2.IsEnabled &&
               config1.IncludeSubdirectories == config2.IncludeSubdirectories;
    }

    private string GetPatternExtension(string pattern)
    {
        if (pattern.StartsWith("*."))
        {
            return pattern[1..];
        }
        return string.Empty;
    }

    private List<string> GetDefaultFilePatterns(string projectType)
    {
        return projectType.ToLower() switch
        {
            "csharp" => new List<string> { "*.cs", "*.csx" },
            "typescript" => new List<string> { "*.ts", "*.tsx" },
            "javascript" => new List<string> { "*.js", "*.jsx" },
            "python" => new List<string> { "*.py" },
            "java" => new List<string> { "*.java" },
            _ => new List<string> { "*.cs" }
        };
    }

    private List<string> GetCommonExcludePatterns(string projectType)
    {
        var common = new List<string> { ".git", "node_modules" };
        
        return projectType.ToLower() switch
        {
            "csharp" => common.Concat(new[] { "bin", "obj", ".vs" }).ToList(),
            "typescript" or "javascript" => common.Concat(new[] { "dist", "build" }).ToList(),
            "python" => common.Concat(new[] { "__pycache__", ".venv", "venv" }).ToList(),
            "java" => common.Concat(new[] { "target", "build" }).ToList(),
            _ => common
        };
    }
}

/// <summary>
/// 配置验证服务接口
/// </summary>
public interface IConfigurationValidationService
{
    ValidationResult ValidateWatchConfiguration(WatchConfigurationDto config);
    ValidationResult ValidateMetadata(MetadataDto metadata);
    ValidationResult ValidateJsonString(string jsonString, string configType);
    CleanupResult CleanupConfiguration(WatchConfigurationDto config);
    List<ConfigurationSuggestion> GetConfigurationSuggestions(WatchConfigurationDto config, string projectType);
}

/// <summary>
/// 清理结果
/// </summary>
public class CleanupResult
{
    public WatchConfigurationDto OriginalConfig { get; set; } = new();
    public WatchConfigurationDto CleanedConfig { get; set; } = new();
    public bool HasChanges { get; set; }
    public List<string> RemovedItems { get; set; } = new();
}

/// <summary>
/// 配置建议
/// </summary>
public class ConfigurationSuggestion
{
    public SuggestionType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SuggestedAction { get; set; } = string.Empty;
    public object? SuggestedValue { get; set; }
}

/// <summary>
/// 建议类型
/// </summary>
public enum SuggestionType
{
    Info,
    Warning,
    Performance,
    Security
}