# Ollama嵌入接口支持实现计划

## 📋 项目概述

为CodebaseMcpServer的Embedding服务新增Ollama嵌入接口支持，使系统能够使用本地运行的Ollama服务生成文本嵌入向量，实现完全本地化的代码语义搜索能力。

## 🎯 需求分析

### 当前架构状态
- ✅ 完善的嵌入向量抽象层：`IEmbeddingProvider`接口
- ✅ 统一的配置管理：`EmbeddingConfiguration`和`EmbeddingProviderSettings` 
- ✅ 工厂模式：`EmbeddingProviderFactory`支持多提供商
- ✅ 现有提供商：DashScope、OpenAI、Azure OpenAI、HuggingFace

### 新增需求
- 🎯 添加Ollama嵌入提供商支持
- 🎯 默认使用nomic-embed-text模型（768维度）
- 🎯 支持本地Ollama服务器连接
- 🎯 集成到现有的配置和工厂管理系统

## 🏗️ 技术架构设计

### 1. Ollama API特点分析
```
Ollama嵌入API特点：
- 本地HTTP服务：默认端口11434
- RESTful API：POST /api/embeddings
- 请求格式：{"model": "nomic-embed-text", "prompt": "text"}
- 响应格式：{"embedding": [float数组]}
- 支持批量处理：多个prompt数组
```

### 2. 核心组件设计

**OllamaEmbeddingProvider实现**：
- 实现`IEmbeddingProvider`接口
- 支持本地Ollama服务器连接
- 处理Ollama特有的API格式
- 支持批量文本处理
- 完善的错误处理和连接检测

## 📊 实施计划

### 阶段一：扩展枚举和配置支持 (15分钟)

#### 1.1 更新EmbeddingProviderType枚举
**文件**：`CodebaseMcpServer/Models/EmbeddingProviderType.cs`

```csharp
public enum EmbeddingProviderType
{
    /// <summary>
    /// Alibaba Cloud DashScope
    /// </summary>
    DashScope,

    /// <summary>
    /// OpenAI official API
    /// </summary>
    OpenAI,

    /// <summary>
    /// Azure OpenAI Service
    /// </summary>
    AzureOpenAI,

    /// <summary>
    /// Hugging Face Inference API
    /// </summary>
    HuggingFace,

    /// <summary>
    /// Ollama本地嵌入服务
    /// </summary>
    Ollama
}
```

#### 1.2 更新appsettings.json配置
**文件**：`CodebaseMcpServer/appsettings.json`

在`EmbeddingProviders.Providers`节点中添加：

```json
"Ollama": {
  "BaseUrl": "http://localhost:11434",
  "Model": "nomic-embed-text",
  "MaxBatchSize": 100,
  "MaxTokenLength": 8192,
  "EmbeddingDimension": 768,
  "Timeout": 60000
}
```

### 阶段二：实现OllamaEmbeddingProvider (45分钟)

#### 2.1 创建OllamaEmbeddingProvider类
**文件**：`CodebaseMcpServer/Services/Embedding/Providers/OllamaEmbeddingProvider.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CodebaseMcpServer.Services.Embedding.Models;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Services.Embedding.Providers
{
    public class OllamaEmbeddingProvider : IEmbeddingProvider
    {
        private readonly EmbeddingProviderSettings _settings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<OllamaEmbeddingProvider> _logger;

        public string ProviderName => "Ollama";

        public OllamaEmbeddingProvider(
            EmbeddingProviderSettings settings,
            IHttpClientFactory httpClientFactory,
            ILogger<OllamaEmbeddingProvider> logger)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (!ValidateConfiguration())
            {
                throw new InvalidOperationException($"Invalid configuration for {ProviderName} embedding provider.");
            }
        }

        public bool ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
            {
                _logger.LogError("{ProviderName}: BaseUrl is missing.", ProviderName);
                return false;
            }
            
            if (string.IsNullOrWhiteSpace(_settings.Model))
            {
                _logger.LogError("{ProviderName}: Model is missing.", ProviderName);
                return false;
            }
            
            if (_settings.MaxBatchSize <= 0)
            {
                _logger.LogError("{ProviderName}: MaxBatchSize must be greater than 0.", ProviderName);
                return false;
            }
            
            if (_settings.EmbeddingDimension <= 0)
            {
                _logger.LogError("{ProviderName}: EmbeddingDimension must be greater than 0.", ProviderName);
                return false;
            }
            
            return true;
        }

        public async Task<List<List<float>>> GetEmbeddingsAsync(List<string> texts)
        {
            if (texts == null || texts.Count == 0)
            {
                _logger.LogWarning("{ProviderName}: Input text list is null or empty.", ProviderName);
                return new List<List<float>>();
            }

            if (texts.Count > _settings.MaxBatchSize)
            {
                _logger.LogError("{ProviderName}: Batch size {ActualSize} exceeds configured maximum {MaxBatchSize}.",
                    ProviderName, texts.Count, _settings.MaxBatchSize);
                throw new ArgumentException($"Batch size cannot exceed configured {_settings.MaxBatchSize}. Current size: {texts.Count}", nameof(texts));
            }

            _logger.LogDebug("{ProviderName}: Requesting embeddings for {Count} texts using model {Model}.", 
                ProviderName, texts.Count, _settings.Model);

            var httpClient = _httpClientFactory.CreateClient(ProviderName);
            httpClient.Timeout = TimeSpan.FromMilliseconds(_settings.Timeout);

            var embeddings = new List<List<float>>();

            try
            {
                // Ollama通常需要逐个处理文本，或者支持批量处理（取决于具体实现）
                foreach (var text in texts)
                {
                    var embedding = await GetSingleEmbeddingAsync(httpClient, text);
                    embeddings.Add(embedding);
                }

                _logger.LogInformation("{ProviderName}: Successfully retrieved {Count} embeddings.", 
                    ProviderName, embeddings.Count);
                return embeddings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ProviderName}: Exception occurred while fetching embeddings.", ProviderName);
                throw;
            }
        }

        private async Task<List<float>> GetSingleEmbeddingAsync(HttpClient httpClient, string text)
        {
            var requestPayload = new
            {
                model = _settings.Model,
                prompt = text
            };

            string baseUrl = _settings.BaseUrl!;
            string requestUrl = baseUrl.TrimEnd('/') + "/api/embeddings";

            var response = await httpClient.PostAsJsonAsync(requestUrl, requestPayload);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var jsonDocument = JsonDocument.Parse(responseContent);
                if (jsonDocument.RootElement.TryGetProperty("embedding", out var embeddingElement))
                {
                    var embedding = new List<float>();
                    foreach (var element in embeddingElement.EnumerateArray())
                    {
                        embedding.Add(element.GetSingle());
                    }
                    return embedding;
                }
                else
                {
                    _logger.LogError("{ProviderName}: Response does not contain 'embedding' property. Response: {Response}", 
                        ProviderName, responseContent);
                    throw new InvalidOperationException($"Invalid response format from {ProviderName}");
                }
            }
            else
            {
                _logger.LogError("Error fetching embedding from {ProviderName}. Status: {StatusCode}. Response: {ErrorContent}",
                    ProviderName, response.StatusCode, responseContent);
                throw new HttpRequestException($"Error fetching embedding from {ProviderName}: {response.StatusCode} - {responseContent}");
            }
        }

        public int GetMaxBatchSize() => _settings.MaxBatchSize > 0 ? _settings.MaxBatchSize : 100;
        public int GetMaxTokenLength() => _settings.MaxTokenLength > 0 ? _settings.MaxTokenLength : 8192;
        public int GetEmbeddingDimension() => _settings.EmbeddingDimension > 0 ? _settings.EmbeddingDimension : 768;
    }
}
```

### 阶段三：集成到工厂和服务 (20分钟)

#### 3.1 更新EmbeddingProviderFactory
**文件**：`CodebaseMcpServer/Services/Embedding/EmbeddingProviderFactory.cs`

在`GetProvider`方法的switch语句中添加Ollama case：

```csharp
case EmbeddingProviderType.Ollama:
    var ollamaHttpClientFactory = _serviceProvider.GetService<IHttpClientFactory>();
    var ollamaLogger = _serviceProvider.GetService<ILogger<Providers.OllamaEmbeddingProvider>>();
    if (ollamaHttpClientFactory == null)
    {
        _logger.LogError("IHttpClientFactory not resolved from service provider. Cannot create OllamaEmbeddingProvider.");
        throw new InvalidOperationException("IHttpClientFactory not available for OllamaEmbeddingProvider.");
    }
    return new Providers.OllamaEmbeddingProvider(providerSettings, ollamaHttpClientFactory, ollamaLogger!);
```

### 阶段四：测试和验证 (20分钟)

#### 4.1 配置验证测试
- 验证Ollama提供商配置正确加载
- 验证枚举值正确解析
- 验证工厂创建Ollama提供商

#### 4.2 功能测试
- 测试单个文本嵌入生成
- 测试批量文本嵌入生成
- 测试错误处理机制

#### 4.3 集成测试
- 验证与现有系统兼容性
- 测试在语义搜索中的使用
- 验证配置切换功能

## 🔧 Ollama API集成细节

### 请求格式
```json
POST /api/embeddings
{
  "model": "nomic-embed-text",
  "prompt": "要嵌入的文本内容"
}
```

### 响应格式
```json
{
  "embedding": [0.1, 0.2, 0.3, ...]
}
```

### 批量处理策略
- 支持单个和批量文本处理
- 智能批量大小管理
- 并发处理优化（如果Ollama支持）

## 🎯 预期收益

### 功能扩展
- ✅ 支持本地Ollama嵌入服务
- ✅ 无需外部API密钥，完全本地化
- ✅ 高性能的nomic-embed-text模型
- ✅ 与现有架构无缝集成

### 技术优势
- 🔒 **数据隐私**：完全本地处理，无数据泄露风险
- ⚡ **性能优化**：本地服务，低延迟响应
- 💰 **成本控制**：无API调用费用
- 🛠️ **易于维护**：统一的配置和管理接口

## ⏱️ 实施时间表

```
总计：约1.5-2小时

阶段一：配置扩展        (15分钟)
阶段二：核心实现        (45分钟)  
阶段三：系统集成        (20分钟)
阶段四：测试验证        (20分钟)
```

## 🔧 前置条件

### 1. Ollama服务安装
- 本地安装Ollama服务
- 下载nomic-embed-text模型：`ollama pull nomic-embed-text`
- 确保Ollama服务运行在默认端口11434

### 2. 开发环境
- 现有CodebaseMcpServer项目
- .NET开发环境
- HTTP客户端库支持（已有）

## 📝 配置示例

### 完整的EmbeddingProviders配置
```json
{
  "EmbeddingProviders": {
    "DefaultProvider": "Ollama",
    "Providers": {
      "DashScope": {
        "ApiKey": "sk-a239bd73d5b947ed955d03d437ca1e70",
        "BaseUrl": "https://dashscope.aliyuncs.com/compatible-mode/v1",
        "Model": "text-embedding-v4",
        "MaxBatchSize": 10,
        "MaxTokenLength": 8192,
        "EmbeddingDimension": 1024,
        "Timeout": 30000
      },
      "Ollama": {
        "BaseUrl": "http://localhost:11434",
        "Model": "nomic-embed-text",
        "MaxBatchSize": 100,
        "MaxTokenLength": 8192,
        "EmbeddingDimension": 768,
        "Timeout": 60000
      },
      "OpenAI": {
        "ApiKey": "",
        "BaseUrl": "https://api.openai.com/v1",
        "Model": "text-embedding-3-small",
        "MaxBatchSize": 2048,
        "MaxTokenLength": 8191,
        "EmbeddingDimension": 1536,
        "Timeout": 30000
      }
    }
  }
}
```

## 🚀 部署说明

### 1. 切换到Ollama提供商
更新`appsettings.json`中的`DefaultProvider`为`"Ollama"`

### 2. 验证Ollama服务
确保本地Ollama服务正常运行：
```bash
curl http://localhost:11434/api/embeddings -d '{
  "model": "nomic-embed-text",
  "prompt": "Hello world"
}'
```

### 3. 重启CodebaseMcpServer
重启服务以加载新配置和提供商

## 🎉 完成标志

- ✅ EmbeddingProviderType枚举包含Ollama选项
- ✅ appsettings.json配置包含Ollama提供商设置  
- ✅ OllamaEmbeddingProvider类实现完成
- ✅ EmbeddingProviderFactory集成Ollama支持
- ✅ 项目编译无错误
- ✅ 基本功能测试通过
- ✅ 与现有系统兼容性验证完成

通过这个实施计划，CodebaseMcpServer将获得完整的Ollama嵌入接口支持，实现真正的本地化代码语义搜索能力。