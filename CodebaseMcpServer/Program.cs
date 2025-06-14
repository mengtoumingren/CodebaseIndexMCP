using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using CodebaseMcpServer.Services;
using CodebaseMcpServer.Tools;
using CodebaseMcpServer.Models;

var builder = Host.CreateApplicationBuilder(args);

// 配置服务
builder.Services.Configure<CodeSearchOptions>(
    builder.Configuration.GetSection("CodeSearch"));

// 添加 MCP 服务器
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CodeSearchTools>();

// 注册代码搜索服务
builder.Services.AddSingleton<ICodeSearchService, CodeSemanticSearch>();

// 配置日志
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// 构建并运行应用
var app = builder.Build();

Console.WriteLine("=== Codebase MCP 服务器启动 ===");
Console.WriteLine("提供语义代码搜索功能");
Console.WriteLine("工具: SemanticCodeSearch - 根据自然语言描述搜索代码片段");
Console.WriteLine("=====================================");

await app.RunAsync();