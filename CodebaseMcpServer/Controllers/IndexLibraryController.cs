using Microsoft.AspNetCore.Mvc;
using CodebaseMcpServer.Services.Domain;
using CodebaseMcpServer.Models.Domain;

namespace CodebaseMcpServer.Controllers;

/// <summary>
/// 索引库管理API控制器
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class IndexLibraryController : ControllerBase
{
    private readonly IIndexLibraryService _indexLibraryService;
    private readonly ILogger<IndexLibraryController> _logger;

    public IndexLibraryController(
        IIndexLibraryService indexLibraryService,
        ILogger<IndexLibraryController> logger)
    {
        _indexLibraryService = indexLibraryService;
        _logger = logger;
    }

    /// <summary>
    /// 获取所有索引库
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<IndexLibraryDto>>> GetAllLibraries()
    {
        try
        {
            var libraries = await _indexLibraryService.GetAllAsync();
            var dtos = libraries.Select(MapToDto).ToList();
            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取索引库列表失败");
            return StatusCode(500, new { message = "获取索引库列表失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 根据ID获取索引库
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<IndexLibraryDto>> GetLibrary(int id)
    {
        try
        {
            var library = await _indexLibraryService.GetByIdAsync(id);
            if (library == null)
            {
                return NotFound(new { message = $"索引库不存在: {id}" });
            }

            return Ok(MapToDto(library));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取索引库失败: {LibraryId}", id);
            return StatusCode(500, new { message = "获取索引库失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 创建新的索引库
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreateIndexLibraryResponse>> CreateLibrary([FromBody] Models.Domain.CreateLibraryRequest request)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await _indexLibraryService.CreateAsync(request);
            
            if (result.IsSuccess)
            {
                var response = new CreateIndexLibraryResponse
                {
                    Success = true,
                    Message = result.Message,
                    Library = MapToDto(result.Library!),
                    TaskId = result.TaskId
                };
                
                return CreatedAtAction(nameof(GetLibrary), new { id = result.Library!.Id }, response);
            }
            else
            {
                return BadRequest(new { message = result.Message });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建索引库失败: {Path}", request.CodebasePath);
            return StatusCode(500, new { message = "创建索引库失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 更新索引库基础信息
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateLibrary(int id, [FromBody] UpdateIndexLibraryRequest request)
    {
        try
        {
            var library = await _indexLibraryService.GetByIdAsync(id);
            if (library == null)
            {
                return NotFound(new { message = $"索引库不存在: {id}" });
            }

            if (!string.IsNullOrEmpty(request.Name))
            {
                library.Name = request.Name;
            }

            var result = await _indexLibraryService.UpdateAsync(library);
            if (result)
            {
                return Ok(new { message = "索引库更新成功" });
            }
            else
            {
                return BadRequest(new { message = "索引库更新失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新索引库失败: {LibraryId}", id);
            return StatusCode(500, new { message = "更新索引库失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 删除索引库
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteLibrary(int id)
    {
        try
        {
            var result = await _indexLibraryService.DeleteAsync(id);
            if (result)
            {
                return Ok(new { message = "索引库删除成功" });
            }
            else
            {
                return NotFound(new { message = $"索引库不存在: {id}" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "删除索引库失败: {LibraryId}", id);
            return StatusCode(500, new { message = "删除索引库失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 更新监控配置
    /// </summary>
    [HttpPut("{id}/watch-config")]
    public async Task<ActionResult> UpdateWatchConfiguration(int id, [FromBody] UpdateWatchConfigurationRequest request)
    {
        try
        {
            var result = await _indexLibraryService.UpdateWatchConfigurationAsync(id, request);
            if (result)
            {
                return Ok(new { message = "监控配置更新成功" });
            }
            else
            {
                return NotFound(new { message = $"索引库不存在: {id}" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新监控配置失败: {LibraryId}", id);
            return StatusCode(500, new { message = "更新监控配置失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 更新元数据
    /// </summary>
    [HttpPut("{id}/metadata")]
    public async Task<ActionResult> UpdateMetadata(int id, [FromBody] UpdateMetadataRequest request)
    {
        try
        {
            var result = await _indexLibraryService.UpdateMetadataAsync(id, request);
            if (result)
            {
                return Ok(new { message = "元数据更新成功" });
            }
            else
            {
                return NotFound(new { message = $"索引库不存在: {id}" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新元数据失败: {LibraryId}", id);
            return StatusCode(500, new { message = "更新元数据失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 更新关联的配置预设
    /// </summary>
    [HttpPut("{id}/presets")]
    public async Task<ActionResult> UpdateLibraryPresets(int id, [FromBody] UpdateLibraryPresetsRequest request)
    {
        try
        {
            if (request == null || request.PresetIds == null)
            {
                return BadRequest(new { message = "请求体无效" });
            }

            var result = await _indexLibraryService.UpdatePresetsAsync(id, request.PresetIds);
            if (result)
            {
                return Ok(new { message = "配置预设更新成功" });
            }
            else
            {
                return BadRequest(new { message = "配置预设更新失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "更新配置预设失败: {LibraryId}", id);
            return StatusCode(500, new { message = "更新配置预设时发生意外错误", error = ex.Message });
        }
    }

    /// <summary>
    /// 开始索引
    /// </summary>
    [HttpPost("{id}/index")]
    public async Task<ActionResult> StartIndexing(int id, [FromBody] StartIndexingRequest? request = null)
    {
        try
        {
            var priority = request?.Priority ?? TaskPriority.Normal;
            var taskId = await _indexLibraryService.StartIndexingAsync(id, priority);
            
            return Ok(new { message = "索引任务已启动", taskId });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动索引失败: {LibraryId}", id);
            return StatusCode(500, new { message = "启动索引失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 重建索引
    /// </summary>
    [HttpPost("{id}/rebuild")]
    public async Task<ActionResult> RebuildIndex(int id)
    {
        try
        {
            var taskId = await _indexLibraryService.RebuildIndexAsync(id);
            return Ok(new { message = "重建索引任务已启动", taskId });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "重建索引失败: {LibraryId}", id);
            return StatusCode(500, new { message = "重建索引失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 停止索引
    /// </summary>
    [HttpPost("{id}/stop")]
    public async Task<ActionResult> StopIndexing(int id)
    {
        try
        {
            var result = await _indexLibraryService.StopIndexingAsync(id);
            if (result)
            {
                return Ok(new { message = "索引任务已停止" });
            }
            else
            {
                return BadRequest(new { message = "停止索引任务失败" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止索引失败: {LibraryId}", id);
            return StatusCode(500, new { message = "停止索引失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取索引库统计信息
    /// </summary>
    [HttpGet("{id}/statistics")]
    public async Task<ActionResult<IndexStatisticsDto>> GetStatistics(int id)
    {
        try
        {
            var statistics = await _indexLibraryService.GetStatisticsAsync(id);
            if (statistics == null)
            {
                return NotFound(new { message = $"索引库不存在: {id}" });
            }

            return Ok(statistics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取统计信息失败: {LibraryId}", id);
            return StatusCode(500, new { message = "获取统计信息失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 按项目类型查询
    /// </summary>
    [HttpGet("by-project-type/{projectType}")]
    public async Task<ActionResult<List<IndexLibraryDto>>> GetByProjectType(string projectType)
    {
        try
        {
            var libraries = await _indexLibraryService.GetByProjectTypeAsync(projectType);
            var dtos = libraries.Select(MapToDto).ToList();
            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "按项目类型查询失败: {ProjectType}", projectType);
            return StatusCode(500, new { message = "查询失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 按团队查询
    /// </summary>
    [HttpGet("by-team/{team}")]
    public async Task<ActionResult<List<IndexLibraryDto>>> GetByTeam(string team)
    {
        try
        {
            var libraries = await _indexLibraryService.GetByTeamAsync(team);
            var dtos = libraries.Select(MapToDto).ToList();
            return Ok(dtos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "按团队查询失败: {Team}", team);
            return StatusCode(500, new { message = "查询失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取全局统计信息
    /// </summary>
    [HttpGet("statistics/global")]
    public async Task<ActionResult<LibraryStatistics>> GetGlobalStatistics()
    {
        try
        {
            var statistics = await _indexLibraryService.GetGlobalStatisticsAsync();
            return Ok(statistics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取全局统计信息失败");
            return StatusCode(500, new { message = "获取统计信息失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取语言分布
    /// </summary>
    [HttpGet("statistics/language-distribution")]
    public async Task<ActionResult<Dictionary<string, int>>> GetLanguageDistribution()
    {
        try
        {
            var distribution = await _indexLibraryService.GetLanguageDistributionAsync();
            return Ok(distribution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取语言分布失败");
            return StatusCode(500, new { message = "获取语言分布失败", error = ex.Message });
        }
    }

    /// <summary>
    /// 获取项目类型分布
    /// </summary>
    [HttpGet("statistics/project-type-distribution")]
    public async Task<ActionResult<Dictionary<string, int>>> GetProjectTypeDistribution()
    {
        try
        {
            var distribution = await _indexLibraryService.GetProjectTypeDistributionAsync();
            return Ok(distribution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取项目类型分布失败");
            return StatusCode(500, new { message = "获取项目类型分布失败", error = ex.Message });
        }
    }

    // 辅助方法
    private static IndexLibraryDto MapToDto(IndexLibrary library)
    {
        return new IndexLibraryDto
        {
            Id = library.Id,
            Name = library.Name,
            CodebasePath = library.CodebasePath,
            CollectionName = library.CollectionName,
            Status = library.Status.ToString(),
            ProjectType = library.MetadataObject.ProjectType,
            Team = library.MetadataObject.Team,
            Framework = library.MetadataObject.Framework,
            Priority = library.MetadataObject.Priority,
            Tags = library.MetadataObject.Tags,
            TotalFiles = library.TotalFiles,
            IndexedSnippets = library.IndexedSnippets,
            IsMonitored = library.WatchConfigObject.IsEnabled,
            FilePatterns = library.WatchConfigObject.FilePatterns,
            ExcludePatterns = library.WatchConfigObject.ExcludePatterns,
            CreatedAt = library.CreatedAt,
            UpdatedAt = library.UpdatedAt,
            LastIndexedAt = library.LastIndexedAt
        };
    }
}

/// <summary>
/// 索引库DTO - 用于API响应
/// </summary>
public class IndexLibraryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CodebasePath { get; set; } = string.Empty;
    public string CollectionName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ProjectType { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string Framework { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
    public int TotalFiles { get; set; }
    public int IndexedSnippets { get; set; }
    public bool IsMonitored { get; set; }
    public List<string> FilePatterns { get; set; } = new();
    public List<string> ExcludePatterns { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastIndexedAt { get; set; }
}

/// <summary>
/// 创建索引库响应
/// </summary>
public class CreateIndexLibraryResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public IndexLibraryDto? Library { get; set; }
    public string? TaskId { get; set; }
}

/// <summary>
/// 更新索引库请求
/// </summary>
public class UpdateIndexLibraryRequest
{
    public string? Name { get; set; }
}

/// <summary>
/// 更新关联预设请求
/// </summary>
public class UpdateLibraryPresetsRequest
{
    public List<string> PresetIds { get; set; } = new();
}

/// <summary>
/// 启动索引请求
/// </summary>
public class StartIndexingRequest
{
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
}