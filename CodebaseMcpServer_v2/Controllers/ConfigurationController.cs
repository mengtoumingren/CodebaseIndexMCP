using Microsoft.AspNetCore.Mvc;
using CodebaseMcpServer.Services.Configuration;
using CodebaseMcpServer.Services.Domain;
using CodebaseMcpServer.Models.Domain;

namespace CodebaseMcpServer.Controllers;

/// <summary>
/// 配置管理API控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ConfigurationController : ControllerBase
{
    private readonly IConfigurationPresetService _presetService;
    private readonly IConfigurationValidationService _validationService;
    private readonly IConfigurationManagementService _managementService;
    private readonly ILogger<ConfigurationController> _logger;

    public ConfigurationController(
        IConfigurationPresetService presetService,
        IConfigurationValidationService validationService,
        IConfigurationManagementService managementService,
        ILogger<ConfigurationController> logger)
    {
        _presetService = presetService;
        _validationService = validationService;
        _managementService = managementService;
        _logger = logger;
    }

    #region 配置预设管理

    /// <summary>
    /// 获取所有预设
    /// </summary>
    [HttpGet("presets")]
    public async Task<ActionResult<List<ConfigurationPreset>>> GetAllPresets()
    {
        try
        {
            var presets = await _presetService.GetAllPresetsAsync();
            return Ok(presets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取预设列表失败");
            return StatusCode(500, new { message = "获取预设列表失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取内置预设
    /// </summary>
    [HttpGet("presets/built-in")]
    public async Task<ActionResult<List<ConfigurationPreset>>> GetBuiltInPresets()
    {
        try
        {
            var presets = await _presetService.GetBuiltInPresetsAsync();
            return Ok(presets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取内置预设失败");
            return StatusCode(500, new { message = "获取内置预设失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取自定义预设
    /// </summary>
    [HttpGet("presets/custom")]
    public async Task<ActionResult<List<ConfigurationPreset>>> GetCustomPresets()
    {
        try
        {
            var presets = await _presetService.GetCustomPresetsAsync();
            return Ok(presets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取自定义预设失败");
            return StatusCode(500, new { message = "获取自定义预设失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 根据ID获取预设
    /// </summary>
    [HttpGet("presets/{id}")]
    public async Task<ActionResult<ConfigurationPreset>> GetPreset(string id)
    {
        try
        {
            var preset = await _presetService.GetPresetByIdAsync(id);
            if (preset == null)
            {
                return NotFound(new { message = $"预设不存在: {id}" });
            }

            return Ok(preset);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取预设失败: {PresetId}", id);
            return StatusCode(500, new { message = "获取预设失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 创建自定义预设
    /// </summary>
    [HttpPost("presets")]
    public async Task<ActionResult> CreateCustomPreset([FromBody] ConfigurationPreset preset)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // 验证预设
            var validation = _presetService.ValidatePreset(preset);
            if (!validation.IsValid)
            {
                return BadRequest(new { message = "预设验证失败", errors = validation.Errors });
            }

            var result = await _presetService.CreateCustomPresetAsync(preset);
            if (result)
            {
                return CreatedAtAction(nameof(GetPreset), new { id = preset.Id }, preset);
            }
            else
            {
                return BadRequest(new { message = "预设创建失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建自定义预设失败: {PresetName}", preset.Name);
            return StatusCode(500, new { message = "创建预设失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 更新自定义预设
    /// </summary>
    [HttpPut("presets/{id}")]
    public async Task<ActionResult> UpdateCustomPreset(string id, [FromBody] ConfigurationPreset preset)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // 验证预设
            var validation = _presetService.ValidatePreset(preset);
            if (!validation.IsValid)
            {
                return BadRequest(new { message = "预设验证失败", errors = validation.Errors });
            }

            var result = await _presetService.UpdateCustomPresetAsync(id, preset);
            if (result)
            {
                return Ok(new { message = "预设更新成功" });
            }
            else
            {
                return NotFound(new { message = $"预设不存在或无法更新: {id}" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新自定义预设失败: {PresetId}", id);
            return StatusCode(500, new { message = "更新预设失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 删除自定义预设
    /// </summary>
    [HttpDelete("presets/{id}")]
    public async Task<ActionResult> DeleteCustomPreset(string id)
    {
        try
        {
            var result = await _presetService.DeleteCustomPresetAsync(id);
            if (result)
            {
                return Ok(new { message = "预设删除成功" });
            }
            else
            {
                return NotFound(new { message = $"预设不存在或无法删除: {id}" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除自定义预设失败: {PresetId}", id);
            return StatusCode(500, new { message = "删除预设失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 根据项目类型获取推荐预设
    /// </summary>
    [HttpGet("presets/recommendations/{projectType}")]
    public async Task<ActionResult<List<ConfigurationPreset>>> GetRecommendedPresets(string projectType)
    {
        try
        {
            var presets = await _presetService.GetRecommendedPresetsAsync(projectType);
            return Ok(presets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取推荐预设失败: {ProjectType}", projectType);
            return StatusCode(500, new { message = "获取推荐预设失败", error = ex.Message });
        }
    }

    #endregion

    #region 配置验证

    /// <summary>
    /// 验证监控配置
    /// </summary>
    [HttpPost("validate/watch-config")]
    public async Task<ActionResult<ValidationResult>> ValidateWatchConfiguration([FromBody] WatchConfigurationDto config)
    {
        try
        {
            var result = await Task.Run(() => _validationService.ValidateWatchConfiguration(config)); // Wrap in Task.Run
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证监控配置失败");
            return StatusCode(500, new { message = "验证配置失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 验证元数据配置
    /// </summary>
    [HttpPost("validate/metadata")]
    public async Task<ActionResult<ValidationResult>> ValidateMetadata([FromBody] MetadataDto metadata)
    {
        try
        {
            var result = await Task.Run(() => _validationService.ValidateMetadata(metadata)); // Wrap in Task.Run
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证元数据失败");
            return StatusCode(500, new { message = "验证元数据失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 验证JSON配置字符串
    /// </summary>
    [HttpPost("validate/json")]
    public async Task<ActionResult<ValidationResult>> ValidateJsonString([FromBody] ValidateJsonRequest request)
    {
        try
        {
            var result = await Task.Run(() => _validationService.ValidateJsonString(request.JsonString, request.ConfigType)); // Wrap in Task.Run
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证JSON配置失败");
            return StatusCode(500, new { message = "验证JSON配置失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 清理和优化配置
    /// </summary>
    [HttpPost("cleanup")]
    public async Task<ActionResult<CleanupResult>> CleanupConfiguration([FromBody] WatchConfigurationDto config)
    {
        try
        {
            var result = await Task.Run(() => _validationService.CleanupConfiguration(config)); // Wrap in Task.Run
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "清理配置失败");
            return StatusCode(500, new { message = "清理配置失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取配置建议
    /// </summary>
    [HttpPost("suggestions")]
    public async Task<ActionResult<List<ConfigurationSuggestion>>> GetConfigurationSuggestions([FromBody] GetSuggestionsRequest request)
    {
        try
        {
            var suggestions = await Task.Run(() => _validationService.GetConfigurationSuggestions(request.Config, request.ProjectType)); // Wrap in Task.Run
            return Ok(suggestions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取配置建议失败");
            return StatusCode(500, new { message = "获取配置建议失败", error = ex.Message });
        }
    }

    #endregion

    #region 配置管理

    /// <summary>
    /// 应用预设到索引库
    /// </summary>
    [HttpPost("apply-preset")]
    public async Task<ActionResult<ConfigurationApplyResult>> ApplyPresetToLibrary([FromBody] ApplyPresetRequest request)
    {
        try
        {
            var result = await _managementService.ApplyPresetToLibraryAsync(request.LibraryId, request.PresetId, request.ValidateOnly);
            
            if (result.IsSuccess)
            {
                return Ok(result);
            }
            else
            {
                return BadRequest(result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "应用预设失败: LibraryId={LibraryId}, PresetId={PresetId}", request.LibraryId, request.PresetId);
            return StatusCode(500, new { message = "应用预设失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取智能配置推荐
    /// </summary>
    [HttpPost("smart-recommendation")]
    public async Task<ActionResult<ConfigurationRecommendation>> GetSmartRecommendation([FromBody] SmartRecommendationRequest request)
    {
        try
        {
            var recommendation = await _managementService.GetSmartRecommendationAsync(request.ProjectPath, request.CurrentProjectType);
            return Ok(recommendation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取智能推荐失败: {ProjectPath}", request.ProjectPath);
            return StatusCode(500, new { message = "获取智能推荐失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 比较配置差异
    /// </summary>
    [HttpPost("compare")]
    public async Task<ActionResult<ConfigurationDiff>> CompareConfigurations([FromBody] CompareConfigurationsRequest request)
    {
        try
        {
            var diff = await Task.Run(() => _managementService.CompareConfigurations(request.Config1, request.Config2, request.Label1, request.Label2)); // Wrap in Task.Run
            return Ok(diff);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "比较配置失败");
            return StatusCode(500, new { message = "比较配置失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 导出索引库配置
    /// </summary>
    [HttpGet("export/{libraryId}")]
    public async Task<ActionResult> ExportLibraryConfiguration(int libraryId, [FromQuery] bool includeStatistics = false)
    {
        try
        {
            var configJson = await _managementService.ExportLibraryConfigurationAsync(libraryId, includeStatistics);
            
            var fileName = $"library-{libraryId}-config-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
            return File(System.Text.Encoding.UTF8.GetBytes(configJson), "application/json", fileName);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出配置失败: {LibraryId}", libraryId);
            return StatusCode(500, new { message = "导出配置失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 导入配置到索引库
    /// </summary>
    [HttpPost("import/{libraryId}")]
    public async Task<ActionResult<ConfigurationImportResult>> ImportLibraryConfiguration(int libraryId, [FromBody] ImportConfigurationRequest request)
    {
        try
        {
            var result = await _managementService.ImportLibraryConfigurationAsync(libraryId, request.ConfigJson, request.ValidateOnly);
            
            if (result.IsSuccess)
            {
                return Ok(result);
            }
            else
            {
                return BadRequest(result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入配置失败: {LibraryId}", libraryId);
            return StatusCode(500, new { message = "导入配置失败", error = ex.Message });
        }
    }

    #endregion

    #region 预设导入导出

    /// <summary>
    /// 导出预设
    /// </summary>
    [HttpGet("presets/{id}/export")]
    public async Task<ActionResult> ExportPreset(string id)
    {
        try
        {
            var presetJson = await _presetService.ExportPresetAsync(id);
            var fileName = $"preset-{id}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
            
            return File(System.Text.Encoding.UTF8.GetBytes(presetJson), "application/json", fileName);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出预设失败: {PresetId}", id);
            return StatusCode(500, new { message = "导出预设失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 导入预设
    /// </summary>
    [HttpPost("presets/import")]
    public async Task<ActionResult> ImportPreset([FromBody] ImportPresetRequest request)
    {
        try
        {
            var result = await _presetService.ImportPresetAsync(request.PresetJson, request.Overwrite);
            
            if (result)
            {
                return Ok(new { message = "预设导入成功" });
            }
            else
            {
                return BadRequest(new { message = "预设导入失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导入预设失败");
            return StatusCode(500, new { message = "导入预设失败", error = ex.Message });
        }
    }

    #endregion
}

#region 请求和响应模型

/// <summary>
/// 验证JSON请求
/// </summary>
public class ValidateJsonRequest
{
    public string JsonString { get; set; } = string.Empty;
    public string ConfigType { get; set; } = string.Empty;
}

/// <summary>
/// 获取建议请求
/// </summary>
public class GetSuggestionsRequest
{
    public WatchConfigurationDto Config { get; set; } = new();
    public string ProjectType { get; set; } = string.Empty;
}

/// <summary>
/// 应用预设请求
/// </summary>
public class ApplyPresetRequest
{
    public int LibraryId { get; set; }
    public string PresetId { get; set; } = string.Empty;
    public bool ValidateOnly { get; set; } = false;
}

/// <summary>
/// 智能推荐请求
/// </summary>
public class SmartRecommendationRequest
{
    public string ProjectPath { get; set; } = string.Empty;
    public string? CurrentProjectType { get; set; }
}

/// <summary>
/// 比较配置请求
/// </summary>
public class CompareConfigurationsRequest
{
    public WatchConfigurationDto Config1 { get; set; } = new();
    public WatchConfigurationDto Config2 { get; set; } = new();
    public string? Label1 { get; set; }
    public string? Label2 { get; set; }
}

/// <summary>
/// 导入配置请求
/// </summary>
public class ImportConfigurationRequest
{
    public string ConfigJson { get; set; } = string.Empty;
    public bool ValidateOnly { get; set; } = false;
}

/// <summary>
/// 导入预设请求
/// </summary>
public class ImportPresetRequest
{
    public string PresetJson { get; set; } = string.Empty;
    public bool Overwrite { get; set; } = false;
}

#endregion