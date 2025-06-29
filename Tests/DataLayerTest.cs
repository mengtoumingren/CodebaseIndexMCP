using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Data;
using CodebaseMcpServer.Services.Data.Repositories;
using CodebaseMcpServer.Services.Migration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Dapper;

namespace CodebaseMcpServer.Tests;

/// <summary>
/// SQLite + JSON 数据层测试
/// </summary>
public class DataLayerTest
{
    private readonly ILogger<DataLayerTest> _logger;
    private readonly DatabaseContext _context;
    private readonly IIndexLibraryRepository _repository;

    public DataLayerTest()
    {
        // 创建测试配置
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["ConnectionStrings:DefaultConnection"] = "Data Source=test-codebase.db"
            })
            .Build();

        // 创建日志
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<DataLayerTest>();
        var contextLogger = loggerFactory.CreateLogger<DatabaseContext>();
        var repoLogger = loggerFactory.CreateLogger<IndexLibraryRepository>();

        // 创建数据库上下文和Repository
        _context = new DatabaseContext(configuration, contextLogger);
        _repository = new IndexLibraryRepository(_context, repoLogger);
    }

    public async Task RunAllTestsAsync()
    {
        try
        {
            Console.WriteLine("🧪 开始SQLite + JSON数据层测试...");
            Console.WriteLine();

            await TestDatabaseInitialization();
            await TestBasicCRUD();
            await TestJsonOperations();
            await TestJsonQueries();
            await TestStatistics();

            Console.WriteLine();
            Console.WriteLine("✅ 所有测试通过！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 测试失败: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        finally
        {
            _context.Dispose();
        }
    }

    private async Task TestDatabaseInitialization()
    {
        Console.WriteLine("📋 测试1: 数据库初始化");
        
        // 数据库应该已经在DatabaseContext构造函数中初始化
        // 测试基本查询
        var count = await _context.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='IndexLibraries'");
        
        if (count > 0)
        {
            Console.WriteLine("  ✅ IndexLibraries表创建成功");
        }
        else
        {
            throw new Exception("IndexLibraries表未创建");
        }
        
        // 测试JSON函数
        var jsonTest = await _context.Connection.QuerySingleAsync<string>("SELECT JSON('{\"test\": true}')");
        if (!string.IsNullOrEmpty(jsonTest))
        {
            Console.WriteLine("  ✅ JSON函数支持正常");
        }
        else
        {
            throw new Exception("JSON函数不支持");
        }
    }

    private async Task TestBasicCRUD()
    {
        Console.WriteLine("📋 测试2: 基础CRUD操作");
        
        // 创建测试数据
        var watchConfig = new WatchConfigurationDto
        {
            FilePatterns = new List<string> { "*.cs", "*.ts" },
            ExcludePatterns = new List<string> { "bin", "obj", ".git" },
            IsEnabled = true,
            MaxFileSize = 10 * 1024 * 1024
        };
        
        var statistics = new StatisticsDto
        {
            TotalFiles = 50,
            IndexedSnippets = 250,
            LastIndexingDuration = 30.5,
            LanguageDistribution = new Dictionary<string, int> { ["csharp"] = 45, ["typescript"] = 5 }
        };
        
        var metadata = new MetadataDto
        {
            ProjectType = "webapi",
            Framework = "net8.0",
            Team = "backend",
            Priority = "high",
            Tags = new List<string> { "microservice", "auth" }
        };
        
        var library = new IndexLibrary
        {
            Name = "测试项目",
            CodebasePath = @"C:\TestProject",
            CollectionName = "test_collection",
            Status = IndexLibraryStatus.Completed,
            WatchConfig = JsonSerializer.Serialize(watchConfig),
            Statistics = JsonSerializer.Serialize(statistics),
            Metadata = JsonSerializer.Serialize(metadata),
            TotalFiles = 50,
            IndexedSnippets = 250
        };

        // Create
        var created = await _repository.CreateAsync(library);
        if (created.Id > 0)
        {
            Console.WriteLine($"  ✅ 创建成功，ID: {created.Id}");
        }
        else
        {
            throw new Exception("创建失败");
        }

        // Read
        var retrieved = await _repository.GetByIdAsync(created.Id);
        if (retrieved != null && retrieved.Name == "测试项目")
        {
            Console.WriteLine("  ✅ 查询成功");
        }
        else
        {
            throw new Exception("查询失败");
        }

        // Update
        retrieved.Name = "更新的测试项目";
        var updated = await _repository.UpdateAsync(retrieved);
        if (updated)
        {
            Console.WriteLine("  ✅ 更新成功");
        }
        else
        {
            throw new Exception("更新失败");
        }

        // Delete (软删除)
        var deleted = await _repository.DeleteAsync(created.Id);
        if (deleted)
        {
            Console.WriteLine("  ✅ 删除成功");
        }
        else
        {
            throw new Exception("删除失败");
        }
    }

    private async Task TestJsonOperations()
    {
        Console.WriteLine("📋 测试3: JSON操作");
        
        // 创建带JSON配置的测试库
        var library = new IndexLibrary
        {
            Name = "JSON测试项目",
            CodebasePath = @"C:\JsonTest",
            CollectionName = "json_test",
            Status = IndexLibraryStatus.Pending
        };
        
        // 设置JSON配置
        var watchConfig = new WatchConfigurationDto
        {
            FilePatterns = new List<string> { "*.py", "*.js" },
            ExcludePatterns = new List<string> { "__pycache__", "node_modules" },
            IsEnabled = true,
            CustomFilters = new List<CustomFilterDto>
            {
                new() { Name = "test-filter", Pattern = "*test*", Enabled = true }
            }
        };
        
        library.WatchConfigObject = watchConfig;
        
        var created = await _repository.CreateAsync(library);
        
        // 测试JSON配置更新
        watchConfig.FilePatterns.Add("*.java");
        var updateResult = await _repository.UpdateWatchConfigAsync(created.Id, watchConfig);
        if (updateResult)
        {
            Console.WriteLine("  ✅ JSON配置更新成功");
        }
        else
        {
            throw new Exception("JSON配置更新失败");
        }
        
        // 验证更新结果
        var updated = await _repository.GetByIdAsync(created.Id);
        var updatedConfig = updated!.WatchConfigObject;
        if (updatedConfig.FilePatterns.Contains("*.java"))
        {
            Console.WriteLine("  ✅ JSON配置验证成功");
        }
        else
        {
            throw new Exception("JSON配置验证失败");
        }
        
        // 测试元数据操作
        await _repository.AppendMetadataAsync(created.Id, "testKey", "testValue");
        Console.WriteLine("  ✅ 元数据追加成功");
        
        // 清理
        await _repository.DeleteAsync(created.Id);
    }

    private async Task TestJsonQueries()
    {
        Console.WriteLine("📋 测试4: JSON查询");
        
        // 创建多个测试库用于查询测试
        var libraries = new List<IndexLibrary>();
        
        for (int i = 1; i <= 3; i++)
        {
            var watchConfig = new WatchConfigurationDto
            {
                FilePatterns = new List<string> { $"*.{(i % 2 == 0 ? "cs" : "py")}" },
                IsEnabled = i != 3 // 第3个设为禁用
            };
            
            var metadata = new MetadataDto
            {
                ProjectType = i % 2 == 0 ? "webapi" : "script",
                Team = i <= 2 ? "teamA" : "teamB",
                Tags = new List<string> { $"tag{i}" }
            };
            
            var library = new IndexLibrary
            {
                Name = $"查询测试项目{i}",
                CodebasePath = $@"C:\QueryTest{i}",
                CollectionName = $"query_test_{i}",
                WatchConfig = JsonSerializer.Serialize(watchConfig),
                Metadata = JsonSerializer.Serialize(metadata)
            };
            
            var created = await _repository.CreateAsync(library);
            libraries.Add(created);
        }
        
        // 测试启用状态查询
        var enabledLibraries = await _repository.GetEnabledLibrariesAsync();
        if (enabledLibraries.Count == 2) // 应该有2个启用的
        {
            Console.WriteLine("  ✅ 启用状态查询成功");
        }
        else
        {
            throw new Exception($"启用状态查询失败，预期2个，实际{enabledLibraries.Count}个");
        }
        
        // 测试项目类型查询
        var webapiLibraries = await _repository.GetByProjectTypeAsync("webapi");
        if (webapiLibraries.Count == 1)
        {
            Console.WriteLine("  ✅ 项目类型查询成功");
        }
        else
        {
            throw new Exception($"项目类型查询失败，预期1个，实际{webapiLibraries.Count}个");
        }
        
        // 测试团队查询
        var teamALibraries = await _repository.GetByTeamAsync("teamA");
        if (teamALibraries.Count == 2)
        {
            Console.WriteLine("  ✅ 团队查询成功");
        }
        else
        {
            throw new Exception($"团队查询失败，预期2个，实际{teamALibraries.Count}个");
        }
        
        // 清理
        foreach (var lib in libraries)
        {
            await _repository.DeleteAsync(lib.Id);
        }
    }

    private async Task TestStatistics()
    {
        Console.WriteLine("📋 测试5: 统计查询");
        
        // 创建一些测试数据
        var library1 = new IndexLibrary
        {
            Name = "统计测试1",
            CodebasePath = @"C:\Stats1",
            CollectionName = "stats_test_1",
            Status = IndexLibraryStatus.Completed,
            TotalFiles = 100,
            IndexedSnippets = 500
        };
        
        var library2 = new IndexLibrary
        {
            Name = "统计测试2",
            CodebasePath = @"C:\Stats2",
            CollectionName = "stats_test_2",
            Status = IndexLibraryStatus.Failed,
            TotalFiles = 50,
            IndexedSnippets = 200
        };
        
        var created1 = await _repository.CreateAsync(library1);
        var created2 = await _repository.CreateAsync(library2);
        
        // 测试统计查询
        var stats = await _repository.GetStatisticsAsync();
        
        if (stats.TotalLibraries >= 2)
        {
            Console.WriteLine($"  ✅ 统计查询成功 - 总库数: {stats.TotalLibraries}");
        }
        else
        {
            throw new Exception("统计查询失败");
        }
        
        if (stats.TotalFiles >= 150 && stats.TotalSnippets >= 700)
        {
            Console.WriteLine($"  ✅ 文件和片段统计正确 - 文件: {stats.TotalFiles}, 片段: {stats.TotalSnippets}");
        }
        else
        {
            throw new Exception($"文件和片段统计错误 - 文件: {stats.TotalFiles}, 片段: {stats.TotalSnippets}");
        }
        
        // 清理
        await _repository.DeleteAsync(created1.Id);
        await _repository.DeleteAsync(created2.Id);
    }

    public static async Task Main(string[] args)
    {
        var test = new DataLayerTest();
        await test.RunAllTestsAsync();
        
        Console.WriteLine();
        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
    }
}