# Codebase MCP 服务器

**Codebase MCP Server** 是一个专为开发者设计的智能代码库搜索引擎。它基于模型上下文协议（MCP），将本地代码库转化为一个可通过自然语言查询的智能知识库。与传统的文件遍历和文本搜索不同，本工具通过先进的语义理解能力，帮助开发者快速、精准地定位代码片段，从而大幅提升开发效率和代码理解深度。

## 核心价值

- **告别繁琐的代码查找**: 无需再手动浏览成百上千的文件，只需用自然语言描述您要找的功能，即可获得最相关的代码。
- **快速理解项目**: 快速掌握新项目或复杂模块的核心逻辑，无论是功能实现、错误处理还是特定算法。
- **提升开发效率**: 将更多时间用于编码，而非在代码的海洋中迷航。

## 功能特性

- **多代码库支持**: 可同时管理和搜索多个代码库的索引。
- **增量索引与自动更新**: 监视文件系统变更，自动更新索引，确保搜索结果始终最新。
- **多嵌入模型支持**: 支持多种嵌入模型提供商（如 DashScope、Ollama 等），灵活适应不同需求。
- **智能代码解析**: 深度解析 C# 代码，识别类、方法、属性等关键结构。
- **持久化任务管理**: 索引任务在后台持久化运行，即使服务器重启也能恢复。
- **MCP 标准协议**: 作为标准的 MCP 服务器，可与任何兼容的 MCP 客户端（如 Claude Desktop）无缝集成。

## 系统要求

- .NET 9.0 或更高版本
- Qdrant 向量数据库 (localhost:6334)
- DashScope API 密钥

## 安装和配置

### 1. 克隆项目

```bash
git clone <项目地址>
cd CodebaseMcpServer
```

### 2. 配置设置

编辑 `appsettings.json` 文件：

```json
{
  "CodeSearch": {
    "DashScopeApiKey": "your-dashscope-api-key",
    "QdrantConfig": {
      "Host": "localhost",
      "Port": 6334,
      "CollectionName": "codebase_embeddings"
    },
    "DefaultCodebasePath": "D:\\Path\\To\\Your\\Codebase",
    "SearchConfig": {
      "DefaultLimit": 10,
      "MaxTokenLength": 8192,
      "BatchSize": 10
    }
  }
}
```

### 3. 启动 Qdrant 数据库

使用 Docker 启动 Qdrant：

```bash
docker run -p 6333:6333 -p 6334:6334 qdrant/qdrant
```

### 4. 构建项目

```bash
dotnet build
```

## 使用方式：核心工作流

典型的使用流程如下，旨在将您的代码库转变为一个可搜索的知识库。

### 步骤 1: 为您的代码库创建索引

首先，需要为目标代码库创建一个向量索引。这是所有搜索功能的基础。

- **工具**: `CreateIndexLibrary`
- **示例**: 假设您的项目位于 `D:\Projects\MyApp`。

```json
{
  "tool_name": "CreateIndexLibrary",
  "arguments": {
    "codebasePath": "D:\\Projects\\MyApp",
    "friendlyName": "My Awesome App"
  }
}
```
服务器将启动一个后台任务来扫描、解析和索引您的代码。

### 步骤 2: 检查索引状态

索引创建需要一些时间，具体取决于代码库的大小。您可以使用 `GetIndexingStatus` 工具来监控进度。

- **工具**: `GetIndexingStatus`
- **示例**:
  - 查看所有索引库的概览：
    ```json
    { "tool_name": "GetIndexingStatus" }
    ```
  - 查看特定代码库的详细状态：
    ```json
    {
      "tool_name": "GetIndexingStatus",
      "arguments": { "codebasePath": "D:\\Projects\\MyApp" }
    }
    ```

### 步骤 3: 执行语义代码搜索

索引完成后，您就可以开始用自然语言进行搜索了。

- **工具**: `SemanticCodeSearch`
- **示例**: 查找用户认证相关的逻辑。

```json
{
  "tool_name": "SemanticCodeSearch",
  "arguments": {
    "query": "用户登录验证逻辑",
    "codebasePath": "D:\\Projects\\MyApp",
    "limit": 5
  }
}
```
服务器将返回最相关的代码片段、文件路径、相似度得分等信息。

### 步骤 4: 管理您的索引库 (可选)

您可以根据需要重建或删除索引。

- **重建索引**: 当代码库发生重大变化或怀疑索引损坏时使用。
  - **工具**: `RebuildIndex`
- **删除索引**: 当不再需要某个代码库的索引时使用。
  - **工具**: `DeleteIndexLibrary` (需要二次确认)

## MCP 工具详解

服务器提供两类工具：**代码搜索**和**索引管理**。

### 代码搜索

#### 1. `SemanticCodeSearch`

**首选代码查询工具**。根据自然语言描述，精准定位相关的代码片段。它避免了完整读取和遍历文件，通过语义相似度直接找到目标代码，极大提升了代码查找和理解的效率。

- **参数**:
  - `query` (string, 必需): 自然语言搜索查询。
    - *高效示例*: `'用户登录验证逻辑'`, `'数据库连接池管理'`, `'JWT令牌生成'`。
    - *避免*: 过于宽泛的查询，如 `'函数'`, `'类'`。
  - `codebasePath` (string, 必需): 要搜索的代码库根目录的绝对路径。
    - *示例*: `'d:/VSProject/MyApp'`, `'./src'`。
  - `limit` (int, 可选, 默认 5): 返回最相关的代码片段数量。
    - *建议*: 快速查找用 5-10 个，详细分析用 15-20 个。

- **使用示例**:
  ```json
  {
    "tool_name": "SemanticCodeSearch",
    "arguments": {
      "query": "如何实现文件上传的错误处理",
      "codebasePath": "D:\\Projects\\WebApp",
      "limit": 3
    }
  }
  ```

### 索引管理

#### 1. `CreateIndexLibrary`

为指定的代码库目录创建语义索引。索引是实现 `SemanticCodeSearch` 的前提。此过程会在后台运行，并自动启用文件监控以进行增量更新。

- **参数**:
  - `codebasePath` (string, 必需): 要索引的代码库目录的完整绝对路径。
  - `friendlyName` (string, 可选): 为索引库指定一个易于识别的名称。如果未提供，则默认使用目录名。

- **使用示例**:
  ```json
  {
    "tool_name": "CreateIndexLibrary",
    "arguments": {
      "codebasePath": "C:\\Users\\Dev\\Documents\\MyProject",
      "friendlyName": "Main Project"
    }
  }
  ```

#### 2. `GetIndexingStatus`

查询一个或所有代码库的索引状态、统计信息和进度。

- **参数**:
  - `codebasePath` (string, 可选): 如果提供，则显示该特定代码库的详细状态。
  - `taskId` (string, 可选): 如果提供，则查询特定索引任务的状态。
  - *注*: 如果两个参数均未提供，则显示所有索引库的状态总览。

- **使用示例**:
  ```json
  {
    "tool_name": "GetIndexingStatus",
    "arguments": {
      "codebasePath": "C:\\Users\\Dev\\Documents\\MyProject"
    }
  }
  ```

#### 3. `RebuildIndex`

当代码结构发生重大变更或怀疑索引数据损坏时，此工具可用于清除旧索引并从头开始重新构建。

- **参数**:
  - `codebasePath` (string, 必需): 要重建索引的代码库路径。

- **使用示例**:
  ```json
  {
    "tool_name": "RebuildIndex",
    "arguments": {
      "codebasePath": "C:\\Users\\Dev\\Documents\\MyProject"
    }
  }
  ```

#### 4. `DeleteIndexLibrary`

永久删除指定代码库的索引数据和相关配置。这是一个危险操作，需要二次确认。

- **参数**:
  - `codebasePath` (string, 必需): 要删除索引的代码库路径。
  - `confirm` (bool, 必需, 默认 `false`): 必须将此参数设置为 `true` 才能执行删除。首次调用（不带或带 `false`）会返回一条确认提示。

- **使用示例 (安全删除)**:
  1.  **首次调用 (获取确认提示)**:
      ```json
      {
        "tool_name": "DeleteIndexLibrary",
        "arguments": { "codebasePath": "C:\\Path\\To\\OldProject" }
      }
      ```
  2.  **二次调用 (确认删除)**:
      ```json
      {
        "tool_name": "DeleteIndexLibrary",
        "arguments": {
          "codebasePath": "C:\\Path\\To\\OldProject",
          "confirm": true
        }
      }
      ```

## MCP 客户端配置

### Claude Desktop 配置

在 Claude Desktop 的配置文件中添加：

```json
{
  "mcpServers": {
    "codebase-search": {
      "url": "http://localhost:5000/sse",
      "alwaysAllow": [
        "SemanticCodeSearch",
        "GetIndexingStatus"
      ],
      "timeout": 30
    }
  }
}
```

### 其他 MCP 客户端

任何支持 MCP 协议的客户端都可以通过标准输入输出与此服务器通信。

## 架构说明

```mermaid
graph TD
    subgraph MCP Client
        A[User/Client Application]
    end

    subgraph CodebaseMcpServer
        B[MCP Protocol Layer]
        C[MCP Tools Layer]
        D[Service Layer]
        E[Data & Infrastructure]
    end

    A -- MCP Request --> B
    B -- Tool Call --> C
    C -- Calls --> D
    D -- Interacts with --> E

    subgraph C [MCP Tools Layer]
        C1[CodeSearchTools]
        C2[IndexManagementTools]
    end

    subgraph D [Service Layer]
        D1[EnhancedCodeSemanticSearch]
        D2[IndexLibraryService]
        D3[FileWatcherService]
        D4[BackgroundTaskService]
    end

    subgraph E [Data & Infrastructure]
        E1[Embedding Providers <br> (DashScope, Ollama, etc.)]
        E2[Qdrant DB <br> (Vector Storage)]
        E3[LiteDB <br> (Metadata & Task Storage)]
        E4[C# Code Parser]
    end

    C1 -- Uses --> D1
    C2 -- Uses --> D2
    
    D1 -- Needs --> E1
    D1 -- Needs --> E2
    D2 -- Manages --> E2
    D2 -- Manages --> E3
    D2 -- Uses --> D3
    D2 -- Uses --> D4
    D1 -- Uses --> E4
```

## 技术栈

- **.NET 9.0**: 运行时环境
- **ModelContextProtocol**: MCP 协议实现
- **Qdrant.Client**: 向量数据库客户端
- **Newtonsoft.Json**: JSON 序列化
- **DashScope API**: 文本嵌入服务
- **Microsoft.Extensions.Hosting**: 应用程序主机

## 开发和扩展

### 添加新工具

1. 在 `Tools/` 目录下创建新的工具类
2. 使用 `[McpServerToolType]` 和 `[McpServerTool]` 特性
3. 在 `Program.cs` 中注册新工具

### 支持新语言

1. 扩展 `CodeSemanticSearch.cs` 中的代码解析逻辑
2. 添加对应语言的正则表达式模式
3. 更新文件模式配置

## 故障排除

### 常见问题

1. **Qdrant 连接失败**
   - 确保 Qdrant 服务正在运行
   - 检查端口 6334 是否可访问

2. **DashScope API 错误**
   - 验证 API 密钥是否正确
   - 检查网络连接

3. **代码库索引失败**
   - 确保代码库路径正确
   - 检查文件读取权限

### 日志记录

服务器会输出详细的调试信息，包括：
- 文件解析进度
- 代码片段提取统计
- 搜索查询日志
- 错误详情

## 许可证

[根据项目需要添加许可证信息]

## 贡献

欢迎提交 Issue 和 Pull Request 来改进此项目。