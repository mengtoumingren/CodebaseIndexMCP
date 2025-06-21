using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Analysis;
using System.Text.Json;

namespace CodebaseMcpServer.Services.Configuration;

/// <summary>
/// 配置预设服务 - 管理项目类型配置模板和自定义预设
/// </summary>
public class ConfigurationPresetService : IConfigurationPresetService
{
    private readonly ILogger<ConfigurationPresetService> _logger;
    private readonly string _presetsPath;

    public ConfigurationPresetService(ILogger<ConfigurationPresetService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _presetsPath = configuration.GetValue<string>("ConfigurationPresets:PresetsPath") 
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config-presets");
        
        EnsurePresetsDirectoryExists();
    }

    /// <summary>
    /// 获取所有内置预设
    /// </summary>
    public async Task<List<ConfigurationPreset>> GetBuiltInPresetsAsync()
    {
        var presets = new List<ConfigurationPreset>();

        foreach (var projectType in Enum.GetValues<ProjectTypeDetector.ProjectType>())
        {
            if (projectType == ProjectTypeDetector.ProjectType.Unknown)
                continue;

            var config = ProjectTypeDetector.ProjectConfigurations.GetValueOrDefault(projectType);
            if (config != null)
            {
                presets.Add(new ConfigurationPreset
                {
                    Id = projectType.ToString().ToLower(),
                    Name = config.Name,
                    Description = config.Description,
                    ProjectType = projectType.ToString().ToLower(),
                    IsBuiltIn = true,
                    Category = GetProjectCategory(projectType),
                    WatchConfiguration = new WatchConfigurationDto
                    {
                        FilePatterns = config.FilePatterns.ToList(),
                        ExcludePatterns = config.ExcludePatterns.ToList(),
                        IncludeSubdirectories = true,
                        IsEnabled = true,
                        MaxFileSize = 10 * 1024 * 1024,
                        CustomFilters = new List<CustomFilterDto>()
                    },
                    Metadata = new MetadataDto
                    {
                        ProjectType = projectType.ToString().ToLower(),
                        Framework = config.Framework,
                        Team = "default",
                        Priority = "normal",
                        Tags = new List<string> { "built-in", projectType.ToString().ToLower() },
                        CustomSettings = new Dictionary<string, object>
                        {
                            ["embeddingModel"] = config.EmbeddingModel,
                            ["isBuiltIn"] = true
                        }
                    },
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        return presets;
    }

    /// <summary>
    /// 获取自定义预设
    /// </summary>
    public async Task<List<ConfigurationPreset>> GetCustomPresetsAsync()
    {
        var presets = new List<ConfigurationPreset>();

        try
        {
            var presetFiles = Directory.GetFiles(_presetsPath, "*.json");
            foreach (var file in presetFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var preset = JsonSerializer.Deserialize<ConfigurationPreset>(json);
                    
                    if (preset != null)
                    {
                        preset.Id = Path.GetFileNameWithoutExtension(file);
                        preset.IsBuiltIn = false;
                        presets.Add(preset);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "加载自定义预设失败: {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "读取预设目录失败: {Path}", _presetsPath);
        }

        return presets;
    }

    /// <summary>
    /// 获取所有预设
    /// </summary>
    public async Task<List<ConfigurationPreset>> GetAllPresetsAsync()
    {
        var builtInPresets = await GetBuiltInPresetsAsync();
        var customPresets = await GetCustomPresetsAsync();
        
        return builtInPresets.Concat(customPresets).ToList();
    }

    /// <summary>
    /// 根据ID获取预设
    /// </summary>
    public async Task<ConfigurationPreset?> GetPresetByIdAsync(string id)
    {
        // 首先检查内置预设
        var builtInPresets = await GetBuiltInPresetsAsync();
        var builtInPreset = builtInPresets.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (builtInPreset != null)
        {
            return builtInPreset;
        }

        // 然后检查自定义预设
        var customPresets = await GetCustomPresetsAsync();
        return customPresets.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 创建自定义预设
    /// </summary>
    public async Task<bool> CreateCustomPresetAsync(ConfigurationPreset preset)
    {
        try
        {
            if (string.IsNullOrEmpty(preset.Id))
            {
                preset.Id = GeneratePresetId(preset.Name);
            }

            // 检查ID是否已存在
            var existing = await GetPresetByIdAsync(preset.Id);
            if (existing != null)
            {
                _logger.LogWarning("预设ID已存在: {Id}", preset.Id);
                return false;
            }

            preset.IsBuiltIn = false;
            preset.CreatedAt = DateTime.UtcNow;
            preset.UpdatedAt = DateTime.UtcNow;

            var filePath = Path.Combine(_presetsPath, $"{preset.Id}.json");
            var json = JsonSerializer.Serialize(preset, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(filePath, json);
            
            _logger.LogInformation("创建自定义预设成功: {Id} - {Name}", preset.Id, preset.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建自定义预设失败: {Name}", preset.Name);
            return false;
        }
    }

    /// <summary>
    /// 更新自定义预设
    /// </summary>
    public async Task<bool> UpdateCustomPresetAsync(string id, ConfigurationPreset preset)
    {
        try
        {
            var existing = await GetPresetByIdAsync(id);
            if (existing == null)
            {
                _logger.LogWarning("要更新的预设不存在: {Id}", id);
                return false;
            }

            if (existing.IsBuiltIn)
            {
                _logger.LogWarning("不能更新内置预设: {Id}", id);
                return false;
            }

            preset.Id = id;
            preset.IsBuiltIn = false;
            preset.CreatedAt = existing.CreatedAt;
            preset.UpdatedAt = DateTime.UtcNow;

            var filePath = Path.Combine(_presetsPath, $"{id}.json");
            var json = JsonSerializer.Serialize(preset, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await File.WriteAllTextAsync(filePath, json);
            
            _logger.LogInformation("更新自定义预设成功: {Id} - {Name}", id, preset.Name);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新自定义预设失败: {Id}", id);
            return false;
        }
    }

    /// <summary>
    /// 删除自定义预设
    /// </summary>
    public async Task<bool> DeleteCustomPresetAsync(string id)
    {
        try
        {
            var existing = await GetPresetByIdAsync(id);
            if (existing == null)
            {
                _logger.LogWarning("要删除的预设不存在: {Id}", id);
                return false;
            }

            if (existing.IsBuiltIn)
            {
                _logger.LogWarning("不能删除内置预设: {Id}", id);
                return false;
            }

            var filePath = Path.Combine(_presetsPath, $"{id}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogInformation("删除自定义预设成功: {Id}", id);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除自定义预设失败: {Id}", id);
            return false;
        }
    }

    /// <summary>
    /// 根据项目类型获取推荐预设
    /// </summary>
    public async Task<List<ConfigurationPreset>> GetRecommendedPresetsAsync(string projectType)
    {
        var allPresets = await GetAllPresetsAsync();
        
        return allPresets
            .Where(p => p.ProjectType.Equals(projectType, StringComparison.OrdinalIgnoreCase) ||
                       p.Tags.Contains(projectType, StringComparer.OrdinalIgnoreCase))
            .OrderBy(p => p.IsBuiltIn ? 0 : 1) // 内置预设优先
            .ThenBy(p => p.Name)
            .ToList();
    }

    /// <summary>
    /// 导出预设
    /// </summary>
    public async Task<string> ExportPresetAsync(string id)
    {
        var preset = await GetPresetByIdAsync(id);
        if (preset == null)
        {
            throw new ArgumentException($"预设不存在: {id}");
        }

        return JsonSerializer.Serialize(preset, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    /// <summary>
    /// 导入预设
    /// </summary>
    public async Task<bool> ImportPresetAsync(string json, bool overwrite = false)
    {
        try
        {
            var preset = JsonSerializer.Deserialize<ConfigurationPreset>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (preset == null)
            {
                _logger.LogWarning("预设JSON格式无效");
                return false;
            }

            // 检查是否已存在
            var existing = await GetPresetByIdAsync(preset.Id);
            if (existing != null && !overwrite)
            {
                _logger.LogWarning("预设已存在，需要设置覆盖标志: {Id}", preset.Id);
                return false;
            }

            if (existing != null && overwrite)
            {
                return await UpdateCustomPresetAsync(preset.Id, preset);
            }
            else
            {
                return await CreateCustomPresetAsync(preset);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入预设失败");
            return false;
        }
    }

    /// <summary>
    /// 验证预设配置
    /// </summary>
    public ValidationResult ValidatePreset(ConfigurationPreset preset)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(preset.Name))
        {
            errors.Add("预设名称不能为空");
        }

        if (preset.WatchConfiguration == null)
        {
            errors.Add("监控配置不能为空");
        }
        else
        {
            if (!preset.WatchConfiguration.FilePatterns.Any())
            {
                errors.Add("至少需要一个文件模式");
            }

            if (preset.WatchConfiguration.MaxFileSize <= 0)
            {
                errors.Add("文件大小限制必须大于0");
            }
        }

        if (preset.Metadata == null)
        {
            errors.Add("元数据配置不能为空");
        }

        return new ValidationResult(errors);
    }

    private void EnsurePresetsDirectoryExists()
    {
        try
        {
            if (!Directory.Exists(_presetsPath))
            {
                Directory.CreateDirectory(_presetsPath);
                _logger.LogInformation("创建预设目录: {Path}", _presetsPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建预设目录失败: {Path}", _presetsPath);
        }
    }

    private string GeneratePresetId(string name)
    {
        // 移除特殊字符，转换为小写，用下划线连接
        var cleanName = new string(name.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray());
        var words = cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var id = string.Join("_", words).ToLower();
        
        // 添加时间戳确保唯一性
        return $"{id}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
    }

    private string GetProjectCategory(ProjectTypeDetector.ProjectType projectType)
    {
        return projectType switch
        {
            ProjectTypeDetector.ProjectType.CSharp => "Backend",
            ProjectTypeDetector.ProjectType.TypeScript => "Frontend",
            ProjectTypeDetector.ProjectType.JavaScript => "Frontend", 
            ProjectTypeDetector.ProjectType.Python => "Data/Backend",
            ProjectTypeDetector.ProjectType.Java => "Backend",
            ProjectTypeDetector.ProjectType.Cpp => "System",
            ProjectTypeDetector.ProjectType.Go => "Backend",
            ProjectTypeDetector.ProjectType.Rust => "System",
            ProjectTypeDetector.ProjectType.Mixed => "Mixed",
            _ => "Other"
        };
    }
}

/// <summary>
/// 配置预设服务接口
/// </summary>
public interface IConfigurationPresetService
{
    Task<List<ConfigurationPreset>> GetBuiltInPresetsAsync();
    Task<List<ConfigurationPreset>> GetCustomPresetsAsync();
    Task<List<ConfigurationPreset>> GetAllPresetsAsync();
    Task<ConfigurationPreset?> GetPresetByIdAsync(string id);
    Task<bool> CreateCustomPresetAsync(ConfigurationPreset preset);
    Task<bool> UpdateCustomPresetAsync(string id, ConfigurationPreset preset);
    Task<bool> DeleteCustomPresetAsync(string id);
    Task<List<ConfigurationPreset>> GetRecommendedPresetsAsync(string projectType);
    Task<string> ExportPresetAsync(string id);
    Task<bool> ImportPresetAsync(string json, bool overwrite = false);
    ValidationResult ValidatePreset(ConfigurationPreset preset);
}

/// <summary>
/// 配置预设模型
/// </summary>
public class ConfigurationPreset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ProjectType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public List<string> Tags { get; set; } = new();
    
    public WatchConfigurationDto WatchConfiguration { get; set; } = new();
    public MetadataDto Metadata { get; set; } = new();
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string CreatedBy { get; set; } = "system";
}

/// <summary>
/// 验证结果
/// </summary>
public class ValidationResult
{
    public bool IsValid => !Errors.Any();
    public List<string> Errors { get; set; } = new();

    public ValidationResult() { }

    public ValidationResult(List<string> errors)
    {
        Errors = errors;
    }
}