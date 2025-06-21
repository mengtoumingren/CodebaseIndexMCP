using CodebaseMcpServer.Models.Domain;
using CodebaseMcpServer.Services.Configuration;
using CodebaseMcpServer.Services.Analysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CodebaseMcpServer.Test;

/// <summary>
/// 配置管理功能测试
/// </summary>
public class ConfigurationManagementTest
{
    private readonly ILogger<ConfigurationManagementTest> _logger;
    private readonly IConfigurationPresetService _presetService;
    private readonly IConfigurationValidationService _validationService;

    public ConfigurationManagementTest()
    {
        // 创建测试配置
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["ConfigurationPresets:PresetsPath"] = "test-presets"
            })
            .Build();

        // 创建日志
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<ConfigurationManagementTest>();
        var presetLogger = loggerFactory.CreateLogger<ConfigurationPresetService>();
        var validationLogger = loggerFactory.CreateLogger<ConfigurationValidationService>();

        // 创建服务
        _presetService = new ConfigurationPresetService(presetLogger, configuration);
        _validationService = new ConfigurationValidationService(validationLogger);
    }

    public async Task RunAllTestsAsync()
    {
        try
        {
            Console.WriteLine("🧪 开始配置管理功能测试...");
            Console.WriteLine();

            await TestBuiltInPresets();
            await TestCustomPresets();
            await TestConfigurationValidation();
            await TestConfigurationCleaning();
            await TestSmartRecommendations();
            await TestPresetImportExport();

            Console.WriteLine();
            Console.WriteLine("✅ 所有配置管理测试通过！");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 测试失败: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    private async Task TestBuiltInPresets()
    {
        Console.WriteLine("📋 测试1: 内置预设功能");
        
        // 获取内置预设
        var builtInPresets = await _presetService.GetBuiltInPresetsAsync();
        
        if (builtInPresets.Count >= 8) // 应该有至少8种项目类型的预设
        {
            Console.WriteLine($"  ✅ 内置预设数量正确: {builtInPresets.Count} 个");
        }
        else
        {
            throw new Exception($"内置预设数量不足，预期至少8个，实际{builtInPresets.Count}个");
        }

        // 验证C#预设
        var csharpPreset = builtInPresets.FirstOrDefault(p => p.ProjectType == "csharp");
        if (csharpPreset != null)
        {
            Console.WriteLine("  ✅ C#预设存在");
            
            if (csharpPreset.WatchConfiguration.FilePatterns.Contains("*.cs"))
            {
                Console.WriteLine("  ✅ C#预设包含正确的文件模式");
            }
            else
            {
                throw new Exception("C#预设缺少*.cs文件模式");
            }

            if (csharpPreset.WatchConfiguration.ExcludePatterns.Contains("bin"))
            {
                Console.WriteLine("  ✅ C#预设包含正确的排除模式");
            }
            else
            {
                throw new Exception("C#预设缺少bin排除模式");
            }
        }
        else
        {
            throw new Exception("未找到C#内置预设");
        }

        // 验证TypeScript预设
        var tsPreset = builtInPresets.FirstOrDefault(p => p.ProjectType == "typescript");
        if (tsPreset != null && tsPreset.WatchConfiguration.FilePatterns.Contains("*.ts"))
        {
            Console.WriteLine("  ✅ TypeScript预设正确");
        }
        else
        {
            throw new Exception("TypeScript预设验证失败");
        }
    }

    private async Task TestCustomPresets()
    {
        Console.WriteLine("📋 测试2: 自定义预设功能");
        
        // 创建自定义预设
        var customPreset = new ConfigurationPreset
        {
            Name = "测试自定义预设",
            Description = "用于测试的自定义预设",
            ProjectType = "custom",
            Category = "Test",
            WatchConfiguration = new WatchConfigurationDto
            {
                FilePatterns = new List<string> { "*.test", "*.spec" },
                ExcludePatterns = new List<string> { "temp", "cache" },
                IsEnabled = true,
                MaxFileSize = 5 * 1024 * 1024
            },
            Metadata = new MetadataDto
            {
                ProjectType = "custom",
                Framework = "test",
                Team = "qa",
                Tags = new List<string> { "test", "custom" }
            }
        };

        // 测试创建
        var createResult = await _presetService.CreateCustomPresetAsync(customPreset);
        if (createResult)
        {
            Console.WriteLine("  ✅ 自定义预设创建成功");
        }
        else
        {
            throw new Exception("自定义预设创建失败");
        }

        // 测试获取
        var retrievedPreset = await _presetService.GetPresetByIdAsync(customPreset.Id);
        if (retrievedPreset != null && retrievedPreset.Name == "测试自定义预设")
        {
            Console.WriteLine("  ✅ 自定义预设获取成功");
        }
        else
        {
            throw new Exception("自定义预设获取失败");
        }

        // 测试更新
        retrievedPreset.Description = "更新后的描述";
        var updateResult = await _presetService.UpdateCustomPresetAsync(retrievedPreset.Id, retrievedPreset);
        if (updateResult)
        {
            Console.WriteLine("  ✅ 自定义预设更新成功");
        }
        else
        {
            throw new Exception("自定义预设更新失败");
        }

        // 测试删除
        var deleteResult = await _presetService.DeleteCustomPresetAsync(customPreset.Id);
        if (deleteResult)
        {
            Console.WriteLine("  ✅ 自定义预设删除成功");
        }
        else
        {
            throw new Exception("自定义预设删除失败");
        }
    }

    private async Task TestConfigurationValidation()
    {
        Console.WriteLine("📋 测试3: 配置验证功能");
        
        // 测试有效配置
        var validConfig = new WatchConfigurationDto
        {
            FilePatterns = new List<string> { "*.cs", "*.ts" },
            ExcludePatterns = new List<string> { "bin", "obj" },
            IsEnabled = true,
            MaxFileSize = 10 * 1024 * 1024,
            IncludeSubdirectories = true,
            CustomFilters = new List<CustomFilterDto>
            {
                new() { Name = "test-filter", Pattern = "*test*", Enabled = true }
            }
        };

        var validResult = _validationService.ValidateWatchConfiguration(validConfig);
        if (validResult.IsValid)
        {
            Console.WriteLine("  ✅ 有效配置验证通过");
        }
        else
        {
            throw new Exception($"有效配置验证失败: {string.Join(", ", validResult.Errors)}");
        }

        // 测试无效配置
        var invalidConfig = new WatchConfigurationDto
        {
            FilePatterns = new List<string>(), // 空文件模式
            ExcludePatterns = new List<string> { "../dangerous" }, // 危险模式
            MaxFileSize = -1, // 无效大小
            CustomFilters = new List<CustomFilterDto>
            {
                new() { Name = "", Pattern = "", Enabled = true } // 空过滤器
            }
        };

        var invalidResult = _validationService.ValidateWatchConfiguration(invalidConfig);
        if (!invalidResult.IsValid && invalidResult.Errors.Count >= 3)
        {
            Console.WriteLine($"  ✅ 无效配置正确识别: {invalidResult.Errors.Count} 个错误");
        }
        else
        {
            throw new Exception("无效配置验证失败");
        }

        // 测试JSON字符串验证
        var validJson = JsonSerializer.Serialize(validConfig);
        var jsonResult = _validationService.ValidateJsonString(validJson, "watchconfig");
        if (jsonResult.IsValid)
        {
            Console.WriteLine("  ✅ JSON字符串验证通过");
        }
        else
        {
            throw new Exception($"JSON字符串验证失败: {string.Join(", ", jsonResult.Errors)}");
        }
    }

    private async Task TestConfigurationCleaning()
    {
        Console.WriteLine("📋 测试4: 配置清理功能");
        
        // 创建需要清理的配置
        var dirtyConfig = new WatchConfigurationDto
        {
            FilePatterns = new List<string> { "*.cs", "*.invalid", "*.ts", "*.cs" }, // 包含无效和重复
            ExcludePatterns = new List<string> { "bin", "../dangerous", "obj" }, // 包含危险模式
            MaxFileSize = 200 * 1024 * 1024, // 超大文件限制
            CustomFilters = new List<CustomFilterDto>
            {
                new() { Name = "valid-filter", Pattern = "*test*", Enabled = true },
                new() { Name = "", Pattern = "invalid", Enabled = true } // 无效过滤器
            }
        };

        var cleanupResult = _validationService.CleanupConfiguration(dirtyConfig);
        
        if (cleanupResult.HasChanges)
        {
            Console.WriteLine("  ✅ 检测到配置需要清理");
        }
        else
        {
            throw new Exception("未检测到需要清理的配置");
        }

        if (cleanupResult.CleanedConfig.FilePatterns.Count == 2 && 
            cleanupResult.CleanedConfig.FilePatterns.Distinct().Count() == 2)
        {
            Console.WriteLine("  ✅ 文件模式清理正确 (去重+无效移除)");
        }
        else
        {
            throw new Exception("文件模式清理失败");
        }

        if (cleanupResult.CleanedConfig.ExcludePatterns.Count == 2 &&
            !cleanupResult.CleanedConfig.ExcludePatterns.Any(p => p.Contains("..")))
        {
            Console.WriteLine("  ✅ 排除模式清理正确 (危险模式移除)");
        }
        else
        {
            throw new Exception("排除模式清理失败");
        }

        if (cleanupResult.CleanedConfig.MaxFileSize <= 100 * 1024 * 1024)
        {
            Console.WriteLine("  ✅ 文件大小限制已修正");
        }
        else
        {
            throw new Exception("文件大小限制修正失败");
        }

        if (cleanupResult.RemovedItems.Count >= 2)
        {
            Console.WriteLine($"  ✅ 移除项目记录正确: {cleanupResult.RemovedItems.Count} 项");
        }
        else
        {
            throw new Exception("移除项目记录不正确");
        }
    }

    private async Task TestSmartRecommendations()
    {
        Console.WriteLine("📋 测试5: 智能推荐功能");
        
        // 测试配置建议
        var testConfig = new WatchConfigurationDto
        {
            FilePatterns = new List<string>(), // 空模式，应该有建议
            ExcludePatterns = new List<string>(),
            MaxFileSize = 60 * 1024 * 1024 // 较大文件，应该有性能建议
        };

        var suggestions = _validationService.GetConfigurationSuggestions(testConfig, "csharp");
        
        if (suggestions.Any(s => s.Type == SuggestionType.Warning))
        {
            Console.WriteLine("  ✅ 检测到警告类型建议");
        }
        else
        {
            throw new Exception("未检测到应有的警告建议");
        }

        var infoSuggestions = suggestions.Where(s => s.Type == SuggestionType.Info).ToList();
        if (infoSuggestions.Any())
        {
            Console.WriteLine($"  ✅ 生成信息类型建议: {infoSuggestions.Count} 个");
        }

        var performanceSuggestions = suggestions.Where(s => s.Type == SuggestionType.Performance).ToList();
        if (performanceSuggestions.Any())
        {
            Console.WriteLine($"  ✅ 生成性能建议: {performanceSuggestions.Count} 个");
        }

        // 测试推荐预设
        var recommendedPresets = await _presetService.GetRecommendedPresetsAsync("csharp");
        if (recommendedPresets.Any())
        {
            Console.WriteLine($"  ✅ 获取C#推荐预设: {recommendedPresets.Count} 个");
        }
        else
        {
            throw new Exception("未找到C#推荐预设");
        }
    }

    private async Task TestPresetImportExport()
    {
        Console.WriteLine("📋 测试6: 预设导入导出功能");
        
        // 创建测试预设
        var testPreset = new ConfigurationPreset
        {
            Id = "export_test",
            Name = "导出测试预设",
            Description = "用于测试导出功能",
            ProjectType = "test",
            WatchConfiguration = new WatchConfigurationDto
            {
                FilePatterns = new List<string> { "*.export" },
                ExcludePatterns = new List<string> { "temp" },
                IsEnabled = true
            },
            Metadata = new MetadataDto
            {
                ProjectType = "test",
                Framework = "test"
            }
        };

        // 创建预设
        await _presetService.CreateCustomPresetAsync(testPreset);

        // 测试导出
        var exportedJson = await _presetService.ExportPresetAsync(testPreset.Id);
        if (!string.IsNullOrEmpty(exportedJson))
        {
            Console.WriteLine("  ✅ 预设导出成功");
        }
        else
        {
            throw new Exception("预设导出失败");
        }

        // 验证导出的JSON格式
        try
        {
            var parsedPreset = JsonSerializer.Deserialize<ConfigurationPreset>(exportedJson);
            if (parsedPreset != null && parsedPreset.Name == testPreset.Name)
            {
                Console.WriteLine("  ✅ 导出的JSON格式正确");
            }
            else
            {
                throw new Exception("导出的JSON格式不正确");
            }
        }
        catch (JsonException)
        {
            throw new Exception("导出的JSON格式无效");
        }

        // 测试导入 (修改ID以避免冲突)
        var modifiedPreset = JsonSerializer.Deserialize<ConfigurationPreset>(exportedJson);
        modifiedPreset!.Id = "import_test";
        modifiedPreset.Name = "导入测试预设";
        var modifiedJson = JsonSerializer.Serialize(modifiedPreset);

        var importResult = await _presetService.ImportPresetAsync(modifiedJson);
        if (importResult)
        {
            Console.WriteLine("  ✅ 预设导入成功");
        }
        else
        {
            throw new Exception("预设导入失败");
        }

        // 验证导入的预设
        var importedPreset = await _presetService.GetPresetByIdAsync("import_test");
        if (importedPreset != null && importedPreset.Name == "导入测试预设")
        {
            Console.WriteLine("  ✅ 导入的预设验证成功");
        }
        else
        {
            throw new Exception("导入的预设验证失败");
        }

        // 清理测试数据
        await _presetService.DeleteCustomPresetAsync(testPreset.Id);
        await _presetService.DeleteCustomPresetAsync("import_test");
    }

    public static async Task Main(string[] args)
    {
        var test = new ConfigurationManagementTest();
        await test.RunAllTestsAsync();
        
        Console.WriteLine();
        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
    }
}