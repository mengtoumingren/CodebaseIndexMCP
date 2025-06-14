# Codebase MCP 服务器

基于现有 CodeSemanticSearch 功能的 Model Context Protocol (MCP) 服务器，提供通过自然语义搜索代码的工具。

## 功能特性

- **语义代码搜索**: 根据自然语言描述搜索相关代码片段
- **智能代码解析**: 支持 C# 代码的类、方法、属性、字段等成员解析
- **向量化索引**: 使用 DashScope Embedding API 和 Qdrant 向量数据库
- **MCP 协议**: 符合 Model Context Protocol 标准，可与 MCP 客户端集成

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

## MCP 工具

### SemanticCodeSearch

根据自然语言描述搜索相关代码片段。

**参数:**
- `query` (必需): 自然语言搜索查询，例如："身份认证逻辑"、"数据库连接"、"文件上传处理"
- `codebasePath` (可选): 要搜索的代码库路径，如果不提供则使用默认配置路径
- `limit` (可选): 返回结果数量限制，默认 10

**示例查询:**
- "身份认证逻辑"
- "数据库连接和查询"
- "文件上传和验证"
- "错误处理机制"
- "缓存实现"

## MCP 客户端配置

### Claude Desktop 配置

在 Claude Desktop 的配置文件中添加：

```json
{
  "mcpServers": {
    "codebase-search": {
      "command": "D:\\Path\\To\\CodebaseMcpServer\\bin\\Debug\\net9.0\\CodebaseMcpServer.exe",
      "args": []
    }
  }
}
```

### 其他 MCP 客户端

任何支持 MCP 协议的客户端都可以通过标准输入输出与此服务器通信。

## 使用示例

1. **启动服务器**:
   ```bash
   dotnet run
   ```

2. **通过 MCP 客户端调用**:
   - 工具名称: `SemanticCodeSearch`
   - 查询示例: "身份认证逻辑"
   - 代码库路径: "D:\\MyProject\\Source" (可选)
   - 结果数量: 5 (可选)

3. **预期输出**:
   ```
   找到 3 个相关代码片段:

   --- 结果 1 (相似度得分: 0.8521) ---
   文件: D:\Project\Auth\UserService.cs
   命名空间: MyApp.Services
   类: UserService
   成员: ValidateUser (方法)
   位置: 第 25-45 行
   代码预览:
   ```csharp
   public async Task<bool> ValidateUser(string username, string password)
   {
       // 身份认证逻辑实现
       var user = await _userRepository.GetByUsernameAsync(username);
       if (user == null) return false;
       
       return _passwordHasher.VerifyPassword(password, user.PasswordHash);
   }
   ```
   ```

## 架构说明

```
MCP Client ──MCP Protocol──> CodebaseMcpServer
                                    │
                                    ├── CodeSearchTools (MCP工具层)
                                    │
                                    ├── CodeSemanticSearch (服务层)
                                    │       │
                                    │       ├── DashScope API (嵌入向量)
                                    │       │
                                    │       └── Qdrant DB (向量存储)
                                    │
                                    └── C# Code Parser (代码解析)
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