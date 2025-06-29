using Microsoft.AspNetCore.Mvc;
using CodebaseMcpServer.Services.Domain;
using CodebaseMcpServer.Models.Domain;

namespace CodebaseMcpServer.Controllers
{
    /// <summary>
    /// 后台任务管理API
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class TaskController : ControllerBase
    {
        private readonly IBackgroundTaskService _taskService;
        private readonly ILogger<TaskController> _logger;

        public TaskController(IBackgroundTaskService taskService, ILogger<TaskController> logger)
        {
            _taskService = taskService;
            _logger = logger;
        }

        /// <summary>
        /// 获取所有后台任务
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<BackgroundTask>>> GetAllTasks()
        {
            try
            {
                var tasks = await _taskService.GetAllTasksAsync();
                return Ok(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取所有任务失败");
                return StatusCode(500, new { message = "获取任务列表失败", error = ex.Message });
            }
        }

        /// <summary>
        /// 获取任务统计摘要
        /// </summary>
        [HttpGet("summary")]
        public async Task<ActionResult<TaskSummaryDto>> GetTaskSummary()
        {
            try
            {
                var summary = await _taskService.GetTaskSummaryAsync();
                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取任务摘要失败");
                return StatusCode(500, new { message = "获取任务摘要失败", error = ex.Message });
            }
        }
    }
}