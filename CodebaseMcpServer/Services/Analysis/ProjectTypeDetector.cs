using CodebaseMcpServer.Models.Domain;

namespace CodebaseMcpServer.Services.Analysis;

/// <summary>
/// 项目类型检测器 - 智能识别项目类型并提供推荐配置
/// </summary>
public class ProjectTypeDetector
{
    private readonly ILogger<ProjectTypeDetector> _logger;

    public ProjectTypeDetector(ILogger<ProjectTypeDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 项目类型枚举
    /// </summary>
    public enum ProjectType
    {
        Unknown,
        CSharp,
        TypeScript,
        JavaScript,
        Python,
        Java,
        Cpp,
        Go,
        Rust,
        Mixed
    }

    /// <summary>
    /// 项目类型配置预设
    /// </summary>
    public static readonly Dictionary<ProjectType, ProjectTypeConfig> ProjectConfigurations = new()
    {
        [ProjectType.CSharp] = new ProjectTypeConfig
        {
            Name = "C# Project",
            FilePatterns = new[] { "*.cs", "*.csx", "*.cshtml", "*.razor" },
            ExcludePatterns = new[] { "bin", "obj", ".vs", ".git", "packages", "*.user" },
            TypicalFiles = new[] { "*.csproj", "*.sln", "Program.cs", "Startup.cs" },
            Framework = "dotnet",
            EmbeddingModel = "text-embedding-3-small",
            Description = "C# .NET项目"
        },
        [ProjectType.TypeScript] = new ProjectTypeConfig
        {
            Name = "TypeScript Project",
            FilePatterns = new[] { "*.ts", "*.tsx", "*.js", "*.jsx" },
            ExcludePatterns = new[] { "node_modules", "dist", "build", ".git", "coverage", "*.d.ts" },
            TypicalFiles = new[] { "package.json", "tsconfig.json", "webpack.config.js", "angular.json" },
            Framework = "node",
            EmbeddingModel = "text-embedding-3-small",
            Description = "TypeScript项目"
        },
        [ProjectType.JavaScript] = new ProjectTypeConfig
        {
            Name = "JavaScript Project",
            FilePatterns = new[] { "*.js", "*.jsx", "*.mjs", "*.vue" },
            ExcludePatterns = new[] { "node_modules", "dist", "build", ".git", "coverage" },
            TypicalFiles = new[] { "package.json", "webpack.config.js", "gulpfile.js" },
            Framework = "node",
            EmbeddingModel = "text-embedding-3-small",
            Description = "JavaScript项目"
        },
        [ProjectType.Python] = new ProjectTypeConfig
        {
            Name = "Python Project",
            FilePatterns = new[] { "*.py", "*.pyi", "*.pyx", "*.ipynb" },
            ExcludePatterns = new[] { "__pycache__", ".venv", "venv", ".git", "dist", "build", "*.pyc" },
            TypicalFiles = new[] { "requirements.txt", "setup.py", "pyproject.toml", "Pipfile" },
            Framework = "python",
            EmbeddingModel = "text-embedding-3-small",
            Description = "Python项目"
        },
        [ProjectType.Java] = new ProjectTypeConfig
        {
            Name = "Java Project",
            FilePatterns = new[] { "*.java", "*.kt", "*.scala" },
            ExcludePatterns = new[] { "target", "build", ".git", "*.class", ".gradle" },
            TypicalFiles = new[] { "pom.xml", "build.gradle", "build.xml" },
            Framework = "jvm",
            EmbeddingModel = "text-embedding-3-small",
            Description = "Java项目"
        },
        [ProjectType.Cpp] = new ProjectTypeConfig
        {
            Name = "C++ Project",
            FilePatterns = new[] { "*.cpp", "*.c", "*.h", "*.hpp", "*.cc", "*.cxx" },
            ExcludePatterns = new[] { "build", "Debug", "Release", ".git", "*.o", "*.obj" },
            TypicalFiles = new[] { "CMakeLists.txt", "Makefile", "*.vcxproj" },
            Framework = "native",
            EmbeddingModel = "text-embedding-3-small",
            Description = "C/C++项目"
        },
        [ProjectType.Go] = new ProjectTypeConfig
        {
            Name = "Go Project",
            FilePatterns = new[] { "*.go" },
            ExcludePatterns = new[] { "vendor", ".git", "bin" },
            TypicalFiles = new[] { "go.mod", "go.sum", "main.go" },
            Framework = "go",
            EmbeddingModel = "text-embedding-3-small",
            Description = "Go项目"
        },
        [ProjectType.Rust] = new ProjectTypeConfig
        {
            Name = "Rust Project",
            FilePatterns = new[] { "*.rs" },
            ExcludePatterns = new[] { "target", ".git", "Cargo.lock" },
            TypicalFiles = new[] { "Cargo.toml", "src/main.rs", "src/lib.rs" },
            Framework = "rust",
            EmbeddingModel = "text-embedding-3-small",
            Description = "Rust项目"
        },
        [ProjectType.Mixed] = new ProjectTypeConfig
        {
            Name = "Mixed Project",
            FilePatterns = new[] { "*.cs", "*.ts", "*.js", "*.py", "*.java", "*.cpp", "*.h", "*.go", "*.rs" },
            ExcludePatterns = new[] { "bin", "obj", "node_modules", "__pycache__", "target", "build", ".git" },
            TypicalFiles = new string[0],
            Framework = "mixed",
            EmbeddingModel = "text-embedding-3-small",
            Description = "混合语言项目"
        }
    };

    /// <summary>
    /// 检测项目类型
    /// </summary>
    public async Task<ProjectTypeDetectionResult> DetectProjectTypeAsync(string codebasePath)
    {
        try
        {
            _logger.LogInformation("开始检测项目类型: {Path}", codebasePath);

            if (!Directory.Exists(codebasePath))
            {
                return new ProjectTypeDetectionResult
                {
                    ProjectType = ProjectType.Unknown,
                    Confidence = 0,
                    Message = "目录不存在"
                };
            }

            var detectionResults = new List<(ProjectType Type, double Score, List<string> Evidence)>();

            // 检测各种项目类型
            detectionResults.Add(await DetectCSharpProjectAsync(codebasePath));
            detectionResults.Add(await DetectTypeScriptProjectAsync(codebasePath));
            detectionResults.Add(await DetectJavaScriptProjectAsync(codebasePath));
            detectionResults.Add(await DetectPythonProjectAsync(codebasePath));
            detectionResults.Add(await DetectJavaProjectAsync(codebasePath));
            detectionResults.Add(await DetectCppProjectAsync(codebasePath));
            detectionResults.Add(await DetectGoProjectAsync(codebasePath));
            detectionResults.Add(await DetectRustProjectAsync(codebasePath));

            // 过滤掉分数为0的结果
            var validResults = detectionResults.Where(r => r.Score > 0).ToList();

            if (!validResults.Any())
            {
                return new ProjectTypeDetectionResult
                {
                    ProjectType = ProjectType.Unknown,
                    Confidence = 0,
                    Message = "未检测到已知的项目类型"
                };
            }

            // 排序并选择最高分
            validResults.Sort((a, b) => b.Score.CompareTo(a.Score));
            var topResult = validResults.First();

            // 如果有多个高分结果，认为是混合项目
            var highScoreResults = validResults.Where(r => r.Score >= topResult.Score * 0.8).ToList();
            
            ProjectType finalType;
            double confidence;
            string message;
            
            if (highScoreResults.Count > 1)
            {
                finalType = ProjectType.Mixed;
                confidence = highScoreResults.Average(r => r.Score);
                message = $"检测到混合项目，主要语言: {string.Join(", ", highScoreResults.Select(r => r.Type))}";
            }
            else
            {
                finalType = topResult.Type;
                confidence = topResult.Score;
                message = $"检测到{ProjectConfigurations[finalType].Name}，证据: {string.Join(", ", topResult.Evidence)}";
            }

            _logger.LogInformation("项目类型检测完成: {Type} (置信度: {Confidence:P0})", finalType, confidence);

            return new ProjectTypeDetectionResult
            {
                ProjectType = finalType,
                Confidence = confidence,
                Message = message,
                Evidence = validResults.SelectMany(r => r.Evidence).ToList(),
                AlternativeTypes = validResults.Skip(1).Take(3).Select(r => r.Type).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "项目类型检测失败: {Path}", codebasePath);
            return new ProjectTypeDetectionResult
            {
                ProjectType = ProjectType.Unknown,
                Confidence = 0,
                Message = $"检测失败: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 获取推荐的监控配置
    /// </summary>
    public WatchConfigurationDto GetRecommendedWatchConfiguration(ProjectType projectType, string codebasePath)
    {
        if (!ProjectConfigurations.TryGetValue(projectType, out var config))
        {
            config = ProjectConfigurations[ProjectType.Mixed];
        }

        return new WatchConfigurationDto
        {
            FilePatterns = config.FilePatterns.ToList(),
            ExcludePatterns = config.ExcludePatterns.ToList(),
            IncludeSubdirectories = true,
            IsEnabled = true,
            MaxFileSize = 10 * 1024 * 1024,
            CustomFilters = new List<CustomFilterDto>()
        };
    }

    /// <summary>
    /// 获取推荐的元数据配置
    /// </summary>
    public MetadataDto GetRecommendedMetadata(ProjectType projectType)
    {
        if (!ProjectConfigurations.TryGetValue(projectType, out var config))
        {
            config = ProjectConfigurations[ProjectType.Mixed];
        }

        return new MetadataDto
        {
            ProjectType = projectType.ToString().ToLower(),
            Framework = config.Framework,
            Team = "default",
            Priority = "normal",
            Tags = new List<string> { "auto-detected", projectType.ToString().ToLower() },
            CustomSettings = new Dictionary<string, object>
            {
                ["embeddingModel"] = config.EmbeddingModel,
                ["autoDetected"] = true,
                ["detectionDate"] = DateTime.UtcNow
            }
        };
    }

    // 具体检测方法
    private async Task<(ProjectType, double, List<string>)> DetectCSharpProjectAsync(string path)
    {
        var evidence = new List<string>();
        double score = 0;

        // 检查项目文件
        var csprojFiles = Directory.GetFiles(path, "*.csproj", SearchOption.AllDirectories);
        var slnFiles = Directory.GetFiles(path, "*.sln", SearchOption.AllDirectories);
        
        if (csprojFiles.Any())
        {
            score += 0.8;
            evidence.Add($"找到{csprojFiles.Length}个.csproj文件");
        }
        
        if (slnFiles.Any())
        {
            score += 0.3;
            evidence.Add($"找到{slnFiles.Length}个.sln文件");
        }

        // 检查代码文件
        var csFiles = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories);
        if (csFiles.Any())
        {
            score += Math.Min(0.5, csFiles.Length * 0.01);
            evidence.Add($"找到{csFiles.Length}个.cs文件");
        }

        return (ProjectType.CSharp, Math.Min(1.0, score), evidence);
    }

    private async Task<(ProjectType, double, List<string>)> DetectTypeScriptProjectAsync(string path)
    {
        var evidence = new List<string>();
        double score = 0;

        if (File.Exists(Path.Combine(path, "tsconfig.json")))
        {
            score += 0.8;
            evidence.Add("找到tsconfig.json");
        }

        if (File.Exists(Path.Combine(path, "package.json")))
        {
            score += 0.3;
            evidence.Add("找到package.json");
        }

        var tsFiles = Directory.GetFiles(path, "*.ts", SearchOption.AllDirectories);
        var tsxFiles = Directory.GetFiles(path, "*.tsx", SearchOption.AllDirectories);
        
        if (tsFiles.Any() || tsxFiles.Any())
        {
            var totalFiles = tsFiles.Length + tsxFiles.Length;
            score += Math.Min(0.6, totalFiles * 0.01);
            evidence.Add($"找到{totalFiles}个TypeScript文件");
        }

        return (ProjectType.TypeScript, Math.Min(1.0, score), evidence);
    }

    private async Task<(ProjectType, double, List<string>)> DetectJavaScriptProjectAsync(string path)
    {
        var evidence = new List<string>();
        double score = 0;

        if (File.Exists(Path.Combine(path, "package.json")))
        {
            score += 0.6;
            evidence.Add("找到package.json");
        }

        var jsFiles = Directory.GetFiles(path, "*.js", SearchOption.AllDirectories);
        if (jsFiles.Any())
        {
            score += Math.Min(0.5, jsFiles.Length * 0.01);
            evidence.Add($"找到{jsFiles.Length}个JavaScript文件");
        }

        return (ProjectType.JavaScript, Math.Min(1.0, score), evidence);
    }

    private async Task<(ProjectType, double, List<string>)> DetectPythonProjectAsync(string path)
    {
        var evidence = new List<string>();
        double score = 0;

        var pythonConfigFiles = new[] { "requirements.txt", "setup.py", "pyproject.toml", "Pipfile" };
        foreach (var configFile in pythonConfigFiles)
        {
            if (File.Exists(Path.Combine(path, configFile)))
            {
                score += 0.4;
                evidence.Add($"找到{configFile}");
            }
        }

        var pyFiles = Directory.GetFiles(path, "*.py", SearchOption.AllDirectories);
        if (pyFiles.Any())
        {
            score += Math.Min(0.6, pyFiles.Length * 0.01);
            evidence.Add($"找到{pyFiles.Length}个Python文件");
        }

        return (ProjectType.Python, Math.Min(1.0, score), evidence);
    }

    private async Task<(ProjectType, double, List<string>)> DetectJavaProjectAsync(string path)
    {
        var evidence = new List<string>();
        double score = 0;

        var javaConfigFiles = new[] { "pom.xml", "build.gradle", "build.xml" };
        foreach (var configFile in javaConfigFiles)
        {
            if (File.Exists(Path.Combine(path, configFile)))
            {
                score += 0.6;
                evidence.Add($"找到{configFile}");
            }
        }

        var javaFiles = Directory.GetFiles(path, "*.java", SearchOption.AllDirectories);
        if (javaFiles.Any())
        {
            score += Math.Min(0.5, javaFiles.Length * 0.01);
            evidence.Add($"找到{javaFiles.Length}个Java文件");
        }

        return (ProjectType.Java, Math.Min(1.0, score), evidence);
    }

    private async Task<(ProjectType, double, List<string>)> DetectCppProjectAsync(string path)
    {
        var evidence = new List<string>();
        double score = 0;

        if (File.Exists(Path.Combine(path, "CMakeLists.txt")) || File.Exists(Path.Combine(path, "Makefile")))
        {
            score += 0.6;
            evidence.Add("找到构建配置文件");
        }

        var cppFiles = Directory.GetFiles(path, "*.cpp", SearchOption.AllDirectories);
        var cFiles = Directory.GetFiles(path, "*.c", SearchOption.AllDirectories);
        var hFiles = Directory.GetFiles(path, "*.h", SearchOption.AllDirectories);
        
        var totalFiles = cppFiles.Length + cFiles.Length + hFiles.Length;
        if (totalFiles > 0)
        {
            score += Math.Min(0.5, totalFiles * 0.01);
            evidence.Add($"找到{totalFiles}个C/C++文件");
        }

        return (ProjectType.Cpp, Math.Min(1.0, score), evidence);
    }

    private async Task<(ProjectType, double, List<string>)> DetectGoProjectAsync(string path)
    {
        var evidence = new List<string>();
        double score = 0;

        if (File.Exists(Path.Combine(path, "go.mod")))
        {
            score += 0.8;
            evidence.Add("找到go.mod");
        }

        var goFiles = Directory.GetFiles(path, "*.go", SearchOption.AllDirectories);
        if (goFiles.Any())
        {
            score += Math.Min(0.5, goFiles.Length * 0.01);
            evidence.Add($"找到{goFiles.Length}个Go文件");
        }

        return (ProjectType.Go, Math.Min(1.0, score), evidence);
    }

    private async Task<(ProjectType, double, List<string>)> DetectRustProjectAsync(string path)
    {
        var evidence = new List<string>();
        double score = 0;

        if (File.Exists(Path.Combine(path, "Cargo.toml")))
        {
            score += 0.8;
            evidence.Add("找到Cargo.toml");
        }

        var rustFiles = Directory.GetFiles(path, "*.rs", SearchOption.AllDirectories);
        if (rustFiles.Any())
        {
            score += Math.Min(0.5, rustFiles.Length * 0.01);
            evidence.Add($"找到{rustFiles.Length}个Rust文件");
        }

        return (ProjectType.Rust, Math.Min(1.0, score), evidence);
    }
}

/// <summary>
/// 项目类型配置
/// </summary>
public class ProjectTypeConfig
{
    public string Name { get; set; } = string.Empty;
    public string[] FilePatterns { get; set; } = Array.Empty<string>();
    public string[] ExcludePatterns { get; set; } = Array.Empty<string>();
    public string[] TypicalFiles { get; set; } = Array.Empty<string>();
    public string Framework { get; set; } = string.Empty;
    public string EmbeddingModel { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// 项目类型检测结果
/// </summary>
public class ProjectTypeDetectionResult
{
    public ProjectTypeDetector.ProjectType ProjectType { get; set; }
    public double Confidence { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Evidence { get; set; } = new();
    public List<ProjectTypeDetector.ProjectType> AlternativeTypes { get; set; } = new();
}