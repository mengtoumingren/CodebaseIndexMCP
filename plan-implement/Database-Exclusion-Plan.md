# 数据库文件监控排除计划

## 概述

此计划详细说明了在 `FileWatcherService` 中排除 `codebase-app.db` 相关文件的更改。此项更改是为了防止数据库文件的频繁更改触发不必要的索引更新，从而提高系统性能和稳定性。

## 实施步骤

1.  **创建 `FilePatternMatcher.cs`**
    *   在 `CodebaseMcpServer/Services/Analysis` 目录下创建一个新的静态类 `FilePatternMatcher`。
    *   实现一个 `IsMatch` 方法，该方法使用 `Microsoft.Extensions.FileSystemGlobbing.Matcher` 来支持 `.gitignore` 风格的通配符匹配。

2.  **修改 `FileWatcherService.cs`**
    *   在 `OnFileChanged` 和 `OnFileRenamed` 方法中，使用 `FilePatternMatcher.IsMatch` 来过滤掉与 `"codebase-app.db*"` 模式匹配的文件。
    *   将 `"codebase-app.db*"` 添加到排除模式列表中。

## 预期结果

*   对 `codebase-app.db`、`codebase-app.db-shm` 和 `codebase-app.db-wal` 文件的任何更改都将被 `FileWatcherService` 忽略。
*   系统的索引过程将不再受到数据库文件写入操作的干扰。