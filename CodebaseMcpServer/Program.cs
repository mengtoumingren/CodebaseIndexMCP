using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CodebaseMcpServer.Tools;
using CodebaseMcpServer.Services;
using ModelContextProtocol.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// 添加日志配置
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// 注册核心服务
builder.Services.AddSingleton<IndexConfigManager>();
builder.Services.AddSingleton<TaskPersistenceService>();
builder.Services.AddSingleton<QdrantConnectionMonitor>();
builder.Services.AddSingleton<EnhancedCodeSemanticSearch>(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    var logger = provider.GetRequiredService<ILogger<EnhancedCodeSemanticSearch>>();
    
    var apiKey = configuration.GetValue<string>("CodeSearch:DashScopeApiKey") ?? "sk-a239bd73d5b947ed955d03d437ca1e70";
    var qdrantHost = configuration.GetValue<string>("CodeSearch:QdrantConfig:Host") ?? "localhost";
    var qdrantPort = configuration.GetValue<int>("CodeSearch:QdrantConfig:Port", 6334);
    
    return new EnhancedCodeSemanticSearch(apiKey, qdrantHost, qdrantPort, logger);
});

builder.Services.AddSingleton<IndexingTaskManager>();
builder.Services.AddHostedService<FileWatcherService>();

// 添加 MCP 服务器配置
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<CodeSearchTools>()
    .WithTools<IndexManagementTools>();

// 构建应用
var app = builder.Build();

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
Console.WriteLine("==========================================");
Console.WriteLine();
Console.WriteLine("🚀 服务状态:");
Console.WriteLine("  ✅ 多代码库索引管理");
Console.WriteLine("  ✅ 语义代码搜索");
Console.WriteLine("  ✅ 文件监控服务");
Console.WriteLine("  ✅ 配置管理 (codebase-indexes.json)");
Console.WriteLine("  ✅ 任务持久化 (task-storage/)");
Console.WriteLine("  ✅ Qdrant连接监控");
Console.WriteLine();
Console.WriteLine("🔧 可用工具:");
Console.WriteLine("  📚 CreateIndexLibrary    - 创建代码库索引");
Console.WriteLine("  🔍 SemanticCodeSearch    - 语义代码搜索");
Console.WriteLine("  📊 GetIndexingStatus     - 查询索引状态");
Console.WriteLine("  📋 ListSearchableCodebases - 列出可搜索代码库");
Console.WriteLine("  🔄 RebuildIndex          - 重建索引");
Console.WriteLine();
Console.WriteLine("⚙️  服务配置:");

try
{
    var configuration = app.Services.GetRequiredService<IConfiguration>();
    var qdrantHost = configuration.GetValue<string>("CodeSearch:QdrantConfig:Host") ?? "localhost";
    var qdrantPort = configuration.GetValue<int>("CodeSearch:QdrantConfig:Port", 6334);
    var enableMonitoring = configuration.GetValue<bool>("FileWatcher:EnableAutoMonitoring", true);
    
    Console.WriteLine($"  📍 Qdrant: {qdrantHost}:{qdrantPort}");
    Console.WriteLine($"  👁️  文件监控: {(enableMonitoring ? "启用" : "禁用")}");
    Console.WriteLine($"  📄 配置文件: codebase-indexes.json");
}
catch (Exception ex)
{
    Console.WriteLine($"  ⚠️  配置读取错误: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("🎯 使用提示:");
Console.WriteLine("  1. 首先使用 CreateIndexLibrary 为代码库创建索引");
Console.WriteLine("  2. 使用 SemanticCodeSearch 搜索代码");
Console.WriteLine("  3. 使用 GetIndexingStatus 查看状态");
Console.WriteLine("  4. 文件变更会自动更新索引");
Console.WriteLine();
Console.WriteLine("==========================================");

// 输出配置文件检查
try
{
    var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "codebase-indexes.json");
    if (File.Exists(configPath))
    {
        var mappings = configManager.GetAllMappings();
        Console.WriteLine($"📚 已发现 {mappings.Count} 个代码库配置");
        
        var completedCount = mappings.Count(m => m.IndexingStatus == "completed");
        if (completedCount > 0)
        {
            Console.WriteLine($"✅ {completedCount} 个代码库可立即搜索");
        }
    }
    else
    {
        Console.WriteLine("📝 配置文件将在首次创建索引时自动生成");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"⚠️  配置检查错误: {ex.Message}");
}

Console.WriteLine("==========================================");
Console.WriteLine();

app.Run();