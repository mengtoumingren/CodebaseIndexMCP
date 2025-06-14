using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CodebaseMcpServer.Tools;

var builder = Host.CreateApplicationBuilder(args);

// 添加 MCP 服务器
builder.Services.AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<CodeSearchTools>();

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
Console.WriteLine("使用 Codebase 项目的 CodeSemanticSearch 类直接执行搜索");
Console.WriteLine("=====================================");

await app.RunAsync();