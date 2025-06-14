# Decision Log

此文件使用列表格式记录架构和实现决策。
2025-06-14 09:30:00 - 更新日志。

*

## 决策

*

## 理由

*

## 实现细节

*
[2025-06-14 10:19:00] - **MCP 服务器架构决策**
## 决策
创建基于现有 CodeSemanticSearch 的轻量级 MCP 控制台应用程序，仅提供 SemanticCodeSearch 工具，移除 IndexCodebase 工具以简化实现。

## 理由
1. 用户明确表示不需要 IndexCodebase 功能
2. 简化架构降低复杂性和维护成本
3. 专注于核心语义搜索功能
4. 利用现有的 CodeSemanticSearch.cs 实现

## 实现细节
- 使用 Microsoft.Extensions.Hosting 创建控制台主机
- 配置 WithStdioServerTransport() 用于标准输入输出通信
- 单一 SemanticCodeSearch 工具支持自然语言代码搜索
- 通过配置文件管理默认代码库路径和 API 密钥