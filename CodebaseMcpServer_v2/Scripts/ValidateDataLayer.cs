using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Data;
using CodebaseMcpServer.Services.Data.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Dapper;

namespace CodebaseMcpServer.Scripts;

/// <summary>
/// 数据层验证脚本
/// </summary>
public static class ValidateDataLayer
{
    public static async Task<bool> ValidateAsync()
    {
        try
        {
            Console.WriteLine("🔍 开始验证SQLite + JSON数据层...");
            
            // 创建配置
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = "Data Source=validation-test.db"
                })
                .Build();

            // 创建日志
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
            var contextLogger = loggerFactory.CreateLogger<DatabaseContext>();
            var repoLogger = loggerFactory.CreateLogger<IndexLibraryRepository>();

            // 创建数据库上下文
            using var context = new DatabaseContext(configuration, contextLogger);
            var repository = new IndexLibraryRepository(context, repoLogger);

            // 1. 测试数据库初始化
            Console.WriteLine("  📋 测试数据库初始化...");
            var tableCount = await context.Connection.QuerySingleAsync<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='IndexLibraries'");
            
            if (tableCount == 0)
            {
                Console.WriteLine("  ❌ IndexLibraries表未创建");
                return false;
            }
            Console.WriteLine("  ✅ 数据库表创建成功");

            // 2. 测试JSON函数
            Console.WriteLine("  📋 测试JSON函数支持...");
            var jsonTest = await context.Connection.QuerySingleAsync<string>("SELECT JSON('{\"test\": true}')");
            if (string.IsNullOrEmpty(jsonTest))
            {
                Console.WriteLine("  ❌ JSON函数不支持");
                return false;
            }
            Console.WriteLine("  ✅ JSON函数支持正常");

            // 3. 测试基础CRUD
            Console.WriteLine("  📋 测试基础CRUD操作...");
            
            var testLibrary = new IndexLibrary
            {
                Name = "验证测试库",
                CodebasePath = @"C:\ValidationTest",
                CollectionName = "validation_test",
                Status = IndexLibraryStatus.Pending,
                WatchConfig = JsonSerializer.Serialize(new WatchConfigurationDto
                {
                    FilePatterns = new List<string> { "*.cs" },
                    ExcludePatterns = new List<string> { "bin", "obj" },
                    IsEnabled = true
                }),
                Statistics = JsonSerializer.Serialize(new StatisticsDto
                {
                    TotalFiles = 10,
                    IndexedSnippets = 50
                }),
                Metadata = JsonSerializer.Serialize(new MetadataDto
                {
                    ProjectType = "console",
                    Team = "test"
                }),
                TotalFiles = 10,
                IndexedSnippets = 50
            };

            // Create
            var created = await repository.CreateAsync(testLibrary);
            if (created.Id <= 0)
            {
                Console.WriteLine("  ❌ 创建操作失败");
                return false;
            }

            // Read
            var retrieved = await repository.GetByIdAsync(created.Id);
            if (retrieved == null || retrieved.Name != "验证测试库")
            {
                Console.WriteLine("  ❌ 查询操作失败");
                return false;
            }

            // Update
            retrieved.Name = "更新的验证测试库";
            var updateResult = await repository.UpdateAsync(retrieved);
            if (!updateResult)
            {
                Console.WriteLine("  ❌ 更新操作失败");
                return false;
            }

            // Delete
            var deleteResult = await repository.DeleteAsync(created.Id);
            if (!deleteResult)
            {
                Console.WriteLine("  ❌ 删除操作失败");
                return false;
            }

            Console.WriteLine("  ✅ CRUD操作成功");

            // 4. 测试JSON查询
            Console.WriteLine("  📋 测试JSON查询功能...");
            
            // 创建测试数据
            var jsonTestLibrary = new IndexLibrary
            {
                Name = "JSON查询测试",
                CodebasePath = @"C:\JsonQueryTest",
                CollectionName = "json_query_test",
                WatchConfig = JsonSerializer.Serialize(new WatchConfigurationDto
                {
                    FilePatterns = new List<string> { "*.py" },
                    IsEnabled = true
                }),
                Metadata = JsonSerializer.Serialize(new MetadataDto
                {
                    ProjectType = "script",
                    Team = "data-team"
                })
            };

            var jsonCreated = await repository.CreateAsync(jsonTestLibrary);

            // 测试项目类型查询
            var scriptLibraries = await repository.GetByProjectTypeAsync("script");
            if (!scriptLibraries.Any(l => l.Id == jsonCreated.Id))
            {
                Console.WriteLine("  ❌ JSON项目类型查询失败");
                return false;
            }

            // 测试启用状态查询
            var enabledLibraries = await repository.GetEnabledLibrariesAsync();
            if (!enabledLibraries.Any(l => l.Id == jsonCreated.Id))
            {
                Console.WriteLine("  ❌ JSON启用状态查询失败");
                return false;
            }

            Console.WriteLine("  ✅ JSON查询功能正常");

            // 清理测试数据
            await repository.DeleteAsync(jsonCreated.Id);

            Console.WriteLine("✅ 所有验证测试通过！");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 验证过程中发生错误: {ex.Message}");
            Console.WriteLine($"详细错误: {ex.StackTrace}");
            return false;
        }
    }

    public static async Task Main(string[] args)
    {
        var success = await ValidateAsync();
        
        if (success)
        {
            Console.WriteLine();
            Console.WriteLine("🎉 SQLite + JSON数据层验证成功！");
            Console.WriteLine("✅ 阶段1：数据存储层重构 - 完成");
            Environment.Exit(0);
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("💥 数据层验证失败，请检查实现");
            Environment.Exit(1);
        }
    }
}