using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using CodebaseMcpServer.Tools;
using CodebaseMcpServer.Services;
using CodebaseMcpServer.Services.Embedding;
using CodebaseMcpServer.Services.Embedding.Models;
using CodebaseMcpServer.Models;
using CodebaseMcpServer.Services.Data;
using CodebaseMcpServer.Services.Data.Repositories;
using CodebaseMcpServer.Services.Migration;
using CodebaseMcpServer.Services.Domain;
using CodebaseMcpServer.Services.Analysis;
using CodebaseMcpServer.Services.Compatibility;
using CodebaseMcpServer.Services.Configuration;
using ModelContextProtocol.AspNetCore;
using System.Net;

HttpClient.DefaultProxy = new WebProxy();
var builder = WebApplication.CreateBuilder(args);

// 添加日志配置
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// =============== 新增：Web API支持 ===============
// 添加控制器支持
builder.Services.AddControllers();

// 添加API文档支持
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { 
        Title = "CodebaseApp API", 
        Version = "v1",
        Description = "CodebaseApp 管理API - 智能代码库索引管理平台"
    });
});

// 添加CORS支持
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

// =============== SQLite + JSON 数据层配置 ===============
// 注册数据库上下文
builder.Services.AddSingleton<DatabaseContext>();

// 注册Repository
builder.Services.AddScoped<IIndexLibraryRepository, IndexLibraryRepository>();

// 注册迁移服务
builder.Services.AddScoped<IJsonMigrationService, JsonMigrationService>();

// =============== 领域服务层配置 ===============
// 注册项目类型检测器
builder.Services.AddSingleton<ProjectTypeDetector>();

// 注册索引库服务
builder.Services.AddScoped<IIndexLibraryService, IndexLibraryService>();

// 注册后台任务服务
builder.Services.AddScoped<IBackgroundTaskService, BackgroundTaskService>();

// 注册兼容性适配器
builder.Services.AddScoped<IndexConfigManagerAdapter>();

// =============== 配置管理服务层 ===============
// 注册配置预设服务
builder.Services.AddScoped<IConfigurationPresetService, ConfigurationPresetService>();

// 注册配置验证服务
builder.Services.AddScoped<IConfigurationValidationService, ConfigurationValidationService>();

// 注册配置管理服务
builder.Services.AddScoped<IConfigurationManagementService, ConfigurationManagementService>();

// =============== 保留现有服务配置 ===============
// 注册核心服务 - 使用兼容性适配器
builder.Services.AddSingleton<IndexConfigManager>(serviceProvider =>
{
    // 代理类将使用 IServiceProvider 来动态解析作用域服务
    var logger = serviceProvider.GetRequiredService<ILogger<IndexConfigManager>>();
    return new IndexConfigManagerProxy(serviceProvider, logger);
});

builder.Services.AddSingleton<TaskPersistenceService>();
builder.Services.AddSingleton<QdrantConnectionMonitor>();

// 配置选项读取
builder.Services.Configure<CodeSearchOptions>(
    builder.Configuration.GetSection("CodeSearch"));
builder.Services.Configure<EmbeddingConfiguration>(
    builder.Configuration.GetSection(EmbeddingConfiguration.ConfigSectionName));

// 注册嵌入向量服务
builder.Services.AddHttpClient();
builder.Services.AddSingleton<EmbeddingProviderFactory>();

// 更新 EnhancedCodeSemanticSearch 注册以使用构造函数注入
builder.Services.AddSingleton<EnhancedCodeSemanticSearch>();

// 注册核心服务（移除循环依赖）
builder.Services.AddSingleton<IndexingTaskManager>();
builder.Services.AddSingleton<FileChangePersistenceService>();
builder.Services.AddSingleton<FileWatcherService>();
builder.Services.AddHostedService<FileWatcherService>(provider => provider.GetRequiredService<FileWatcherService>());

// 添加 MCP 服务器配置
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<CodeSearchTools>()
    .WithTools<IndexManagementTools>();

// 构建应用
var app = builder.Build();

// =============== 新增：Web界面配置 ===============
// 启用开发环境特性
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CodebaseApp API v1");
        c.RoutePrefix = "api-docs";
    });
}

// 启用CORS
app.UseCors();

// 启用静态文件服务
app.UseStaticFiles();

// 启用路由
app.UseRouting();

// 映射控制器
app.MapControllers();

// 映射默认路由到管理界面
app.MapFallbackToFile("index.html");

// =============== 数据迁移 ===============
// 执行数据迁移
try
{
    using var scope = app.Services.CreateScope();
    var migrationService = scope.ServiceProvider.GetRequiredService<IJsonMigrationService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    logger.LogInformation("开始检查和执行数据迁移...");
    var migrationResult = await migrationService.MigrateFromLegacyAsync();
    
    if (migrationResult.Success)
    {
        logger.LogInformation("数据迁移完成: {Message}", migrationResult.Message);
        if (migrationResult.MigratedLibraries.Any())
        {
            logger.LogInformation("迁移的索引库:");
            foreach (var lib in migrationResult.MigratedLibraries)
            {
                logger.LogInformation("  - {Name}: {Path}", lib.Name, lib.CodebasePath);
            }
        }
    }
    else
    {
        logger.LogError("数据迁移失败: {Message}", migrationResult.Message);
    }
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "数据迁移过程中发生异常");
}

// =============== 配置预设初始化 ===============
// 初始化配置预设
try
{
    using var scope = app.Services.CreateScope();
    var presetService = scope.ServiceProvider.GetRequiredService<IConfigurationPresetService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    logger.LogInformation("检查配置预设...");
    var builtInPresets = await presetService.GetBuiltInPresetsAsync();
    var customPresets = await presetService.GetCustomPresetsAsync();
    
    logger.LogInformation("发现 {BuiltIn} 个内置预设, {Custom} 个自定义预设", 
        builtInPresets.Count, customPresets.Count);
}
catch (Exception ex)
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "配置预设初始化过程中发生异常");
}

// 初始化工具依赖
var serviceProvider = app.Services;
var configManager = serviceProvider.GetRequiredService<IndexConfigManager>();
var searchService = serviceProvider.GetRequiredService<EnhancedCodeSemanticSearch>();
var taskManager = serviceProvider.GetRequiredService<IndexingTaskManager>();

// 初始化MCP工具
CodeSearchTools.Initialize(searchService, configManager);
IndexManagementTools.Initialize(taskManager, configManager);

app.MapMcp();

// 输出启动信息
Console.WriteLine("==========================================");
Console.WriteLine("===   升级版 Codebase MCP 服务器      ===");
Console.WriteLine("===     阶段4: Web管理界面完成        ===");
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine("🚀 服务状态:");
Console.WriteLine("  ✅ SQLite + JSON 混合数据存储");
Console.WriteLine("  ✅ 智能项目类型检测");
Console.WriteLine("  ✅ 新一代索引库服务");
Console.WriteLine("  ✅ 配置预设管理系统");
Console.WriteLine("  ✅ 配置验证和清理");
Console.WriteLine("  ✅ 智能配置推荐");
Console.WriteLine("  ✅ 配置导入导出");
Console.WriteLine("  ✅ Web管理界面 (全新)");
Console.WriteLine("  ✅ RESTful API接口");
Console.WriteLine("  ✅ 兼容性适配器 (现有MCP工具无缝切换)");
Console.WriteLine("  ✅ 多代码库索引管理");
Console.WriteLine("  ✅ 语义代码搜索");
Console.WriteLine("  ✅ 文件监控服务");
Console.WriteLine("  ✅ 数据库自动迁移");
Console.WriteLine("  ✅ 任务持久化");
Console.WriteLine("  ✅ Qdrant连接监控");
Console.WriteLine();
Console.WriteLine("🔧 可用工具:");
Console.WriteLine("  📚 CreateIndexLibrary    - 创建代码库索引 (支持智能检测+预设应用)");
Console.WriteLine("  🔍 SemanticCodeSearch    - 语义代码搜索");
Console.WriteLine("  📊 GetIndexingStatus     - 查询索引状态");
Console.WriteLine("  📋 ListSearchableCodebases - 列出可搜索代码库");
Console.WriteLine("  🔄 RebuildIndex          - 重建索引");
Console.WriteLine();
Console.WriteLine("🆕 阶段4新增功能:");
Console.WriteLine("  🌐 Web管理控制台 (现代化单页应用)");
Console.WriteLine("  📊 实时仪表板 (统计数据可视化)");
Console.WriteLine("  📚 索引库可视化管理 (增删改查)");
Console.WriteLine("  📝 配置预设可视化管理");
Console.WriteLine("  🎯 智能索引库创建向导");
Console.WriteLine("  📤 配置导入导出界面");
Console.WriteLine("  📖 完整的API文档 (Swagger)");
Console.WriteLine();
Console.WriteLine("⚙️  服务配置:");

try
{
    var configuration = app.Services.GetRequiredService<IConfiguration>();
    var qdrantHost = configuration.GetValue<string>("CodeSearch:QdrantConfig:Host") ?? "localhost";
    var qdrantPort = configuration.GetValue<int>("CodeSearch:QdrantConfig:Port", 6334);
    var enableMonitoring = configuration.GetValue<bool>("FileWatcher:EnableAutoMonitoring", true);
    var dbPath = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=codebase-app.db";
    var presetsPath = configuration.GetValue<string>("ConfigurationPresets:PresetsPath") ?? "config-presets";
    var enableValidation = configuration.GetValue<bool>("ConfigurationValidation:EnableSecurityValidation", true);
    var webInterfaceEnabled = configuration.GetValue<bool>("WebInterface:Enabled", true);
    
    Console.WriteLine($"  📍 Qdrant: {qdrantHost}:{qdrantPort}");
    Console.WriteLine($"  🗄️  数据库: {dbPath}");
    Console.WriteLine($"  👁️  文件监控: {(enableMonitoring ? "启用" : "禁用")}");
    Console.WriteLine($"  📝 配置预设: {presetsPath}");
    Console.WriteLine($"  🔒 安全验证: {(enableValidation ? "启用" : "禁用")}");
    Console.WriteLine($"  🌐 Web界面: {(webInterfaceEnabled ? "启用" : "禁用")}");
    Console.WriteLine($"  🧠 项目检测: 支持9种项目类型");
}
catch (Exception ex)
{
    Console.WriteLine($"  ⚠️  配置读取错误: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("🌐 Web管理界面:");
Console.WriteLine($"  📊 管理控制台: http://localhost:5000");
Console.WriteLine($"  📖 API文档: http://localhost:5000/api-docs");
Console.WriteLine($"  🔗 API根路径: http://localhost:5000/api");
Console.WriteLine();
Console.WriteLine("🎯 使用提示:");
Console.WriteLine("  1. 访问 Web管理界面 进行可视化管理");
Console.WriteLine("  2. 使用仪表板查看系统状态和统计信息");
Console.WriteLine("  3. 通过界面创建索引库 (支持智能检测+预设)");
Console.WriteLine("  4. 管理配置预设和导入导出");
Console.WriteLine("  5. 查看API文档了解编程接口");
Console.WriteLine("  6. 现有MCP工具继续兼容工作");
Console.WriteLine("  7. 文件变更会自动更新索引");
Console.WriteLine();
Console.WriteLine("==========================================");

// 输出数据库状态检查
try
{
    using var scope = app.Services.CreateScope();
    var indexLibraryService = scope.ServiceProvider.GetRequiredService<IIndexLibraryService>();
    var presetService = scope.ServiceProvider.GetRequiredService<IConfigurationPresetService>();
    
    var libraries = await indexLibraryService.GetAllAsync();
    var allPresets = await presetService.GetAllPresetsAsync();
    var builtInPresets = allPresets.Where(p => p.IsBuiltIn).ToList();
    var customPresets = allPresets.Where(p => !p.IsBuiltIn).ToList();
    
    Console.WriteLine($"📚 数据库中发现 {libraries.Count} 个索引库配置");
    Console.WriteLine($"📝 配置预设: {builtInPresets.Count} 个内置, {customPresets.Count} 个自定义");
    
    var completedCount = libraries.Count(l => l.Status == CodebaseMcpServer.Models.Domain.IndexLibraryStatus.Completed);
    if (completedCount > 0)
    {
        Console.WriteLine($"✅ {completedCount} 个代码库可立即搜索");
    }
    
    var enabledCount = libraries.Count(l => l.WatchConfigObject.IsEnabled);
    if (enabledCount > 0)
    {
        Console.WriteLine($"👁️  {enabledCount} 个代码库启用了文件监控");
    }
    
    // 显示项目类型分布
    var typeDistribution = await indexLibraryService.GetProjectTypeDistributionAsync();
    if (typeDistribution.Any())
    {
        Console.WriteLine("🏷️  项目类型分布:");
        foreach (var type in typeDistribution.OrderByDescending(x => x.Value))
        {
            Console.WriteLine($"    {type.Key}: {type.Value} 个项目");
        }
    }
    
    // 显示内置预设
    if (builtInPresets.Any())
    {
        Console.WriteLine("📋 可用内置预设:");
        foreach (var preset in builtInPresets.Take(5))
        {
            Console.WriteLine($"  📝 {preset.Name} ({preset.Category}) - {preset.WatchConfiguration.FilePatterns.Count} 种文件类型");
        }
        if (builtInPresets.Count > 5)
        {
            Console.WriteLine($"  ... 还有 {builtInPresets.Count - 5} 个内置预设");
        }
    }
    
    if (libraries.Any())
    {
        Console.WriteLine("📋 索引库列表:");
        foreach (var lib in libraries.Take(3)) // 只显示前3个
        {
            var status = lib.Status switch
            {
                CodebaseMcpServer.Models.Domain.IndexLibraryStatus.Completed => "✅",
                CodebaseMcpServer.Models.Domain.IndexLibraryStatus.Indexing => "⏳",
                CodebaseMcpServer.Models.Domain.IndexLibraryStatus.Failed => "❌",
                CodebaseMcpServer.Models.Domain.IndexLibraryStatus.Pending => "⏸️",
                _ => "❓"
            };
            var projectType = lib.MetadataObject.ProjectType;
            var filePatternCount = lib.WatchConfigObject.FilePatterns.Count;
            Console.WriteLine($"  {status} {lib.Name} ({projectType}) - {lib.TotalFiles} 文件, {filePatternCount} 种类型模式");
        }
        
        if (libraries.Count > 3)
        {
            Console.WriteLine($"  ... 更多信息请访问Web管理界面");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️  数据库状态检查错误: {ex.Message}");
}

Console.WriteLine("==========================================");
Console.WriteLine();

app.Run();

/// <summary>
/// IndexConfigManager代理类 - 内部使用适配器实现兼容性
/// </summary>
public class IndexConfigManagerProxy : IndexConfigManager
{
    private readonly IServiceProvider _serviceProvider;

    public IndexConfigManagerProxy(IServiceProvider serviceProvider, ILogger<IndexConfigManager> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
    }

    // 重写关键方法，在独立作用域内解析适配器并委托调用
    public new async Task<bool> AddCodebaseMapping(CodebaseMcpServer.Services.Domain.CodebaseMapping mapping)
    {
        using var scope = _serviceProvider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IndexConfigManagerAdapter>();
        return await adapter.AddCodebaseMapping(mapping);
    }

    public new CodebaseMcpServer.Services.Domain.CodebaseMapping? GetMappingByPath(string path)
    {
        using var scope = _serviceProvider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IndexConfigManagerAdapter>();
        return adapter.GetMappingByPath(path);
    }

    public new CodebaseMcpServer.Services.Domain.CodebaseMapping? GetMappingByPathWithParentFallback(string path)
    {
        using var scope = _serviceProvider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IndexConfigManagerAdapter>();
        return adapter.GetMappingByPathWithParentFallback(path);
    }

    public new CodebaseMcpServer.Services.Domain.CodebaseMapping? GetMappingByCollection(string collectionName)
    {
        using var scope = _serviceProvider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IndexConfigManagerAdapter>();
        return adapter.GetMappingByCollection(collectionName);
    }

    public new async Task<bool> UpdateMapping(CodebaseMcpServer.Services.Domain.CodebaseMapping updatedMapping)
    {
        using var scope = _serviceProvider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IndexConfigManagerAdapter>();
        return await adapter.UpdateMapping(updatedMapping);
    }

    public new async Task<bool> RemoveMapping(string id)
    {
        using var scope = _serviceProvider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IndexConfigManagerAdapter>();
        return await adapter.RemoveMapping(id);
    }

    public new async Task<bool> RemoveMappingByPath(string codebasePath)
    {
        using var scope = _serviceProvider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IndexConfigManagerAdapter>();
        return await adapter.RemoveMappingByPath(codebasePath);
    }

    public new List<CodebaseMcpServer.Services.Domain.CodebaseMapping> GetAllMappings()
    {
        using var scope = _serviceProvider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IndexConfigManagerAdapter>();
        return adapter.GetAllMappings();
    }

    public new List<CodebaseMcpServer.Services.Domain.CodebaseMapping> GetMonitoredMappings()
    {
        using var scope = _serviceProvider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IndexConfigManagerAdapter>();
        return adapter.GetMonitoredMappings();
    }

    public new async Task<CodebaseMcpServer.Models.IndexConfiguration> GetConfiguration()
    {
        using var scope = _serviceProvider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IndexConfigManagerAdapter>();
        return await adapter.GetConfiguration();
    }

    public new async Task<bool> UpdateMappingStatistics(string id, Action<CodebaseMcpServer.Services.Domain.IndexStatistics> updateAction)
    {
        using var scope = _serviceProvider.CreateScope();
        var adapter = scope.ServiceProvider.GetRequiredService<IndexConfigManagerAdapter>();
        return await adapter.UpdateMappingStatistics(id, updateAction);
    }
}