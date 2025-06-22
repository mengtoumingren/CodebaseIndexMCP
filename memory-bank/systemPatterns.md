# System Patterns *Optional*

此文件记录项目中使用的重复模式和标准。
它是可选的，但建议随着项目的发展而更新。
2025-06-14 09:30:00 - 更新日志。

*

## 编码模式

*   

## 架构模式

*   

## 测试模式

*
[2025-06-22 21:47:13] - 新增单元测试规范与执行方式：
- 测试类命名：xxxTests
- 测试方法命名：MethodName_Scenario_ExpectedResult
- 使用 [Fact] 或 [Theory] 标记测试方法
- 执行全部测试：dotnet test
- 执行指定类/方法：dotnet test --filter FullyQualifiedName~CodebaseMcpServer.Tests.类名.方法名