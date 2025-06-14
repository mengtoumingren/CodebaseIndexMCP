# 增量重建索引功能实施计划

**版本：** 1.0
**日期：** 2025-06-15

## 1. 目标

改进代码库索引的 `RebuildIndex` 功能，使其能够基于文件上次索引时间和当前文件状态（修改、新增、删除）进行增量更新，避免全量重建整个代码库的索引，以提高效率和减少不必要的计算资源消耗。

## 2. 核心变更点

### 2.1. 数据模型更新 (`CodebaseMcpServer/Models/IndexConfiguration.cs`)

在 `CodebaseMapping` 类中添加一个新的属性 `FileIndexDetails`，用于存储代码库中每个被索引文件的详细信息。

```csharp
// In CodebaseMcpServer/Models/IndexConfiguration.cs

// Potentially add: using System.Text.Json.Serialization;

public class CodebaseMapping
{
    // ... existing properties ...

    [JsonPropertyName("fileIndexDetails")]
    public List<FileIndexDetail> FileIndexDetails { get; set; } = new(); // 新增
}

// 新增类
public class FileIndexDetail
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty; // 文件相对路径 (相对于 codebasePath)

    [JsonPropertyName("normalizedFilePath")]
    public string NormalizedFilePath { get; set; } = string.Empty; // 规范化的文件相对路径

    [JsonPropertyName("lastIndexed")]
    public DateTime LastIndexed { get; set; } // 文件上次成功索引的时间 (UTC)

    [JsonPropertyName("fileHash")] // 可选，用于更精确地判断文件内容是否变化
    public string? FileHash { get; set; } 
}
```

**说明：**

*   `FilePath`：存储相对于 `CodebaseMapping.CodebasePath` 的文件路径。
*   `NormalizedFilePath`：存储规范化后的相对路径，用于可靠比较。
*   `LastIndexed`：记录该文件最后一次被成功索引或确认未更改的时间。
*   `FileHash`：(可选) 存储文件内容的哈希值 (如 SHA256)，用于在文件修改时间戳不可靠或未改变但内容实际已变时，提供更精确的变更检测。

### 2.2. `IndexingTaskManager.cs` 逻辑更新

#### 2.2.1. `ExecuteIndexingTaskAsync` (创建新索引时)

*   在对一个新的代码库完整执行索引流程后，遍历所有被成功处理并索引的文件。
*   为每个成功索引的文件：
    *   计算其相对于 `codebasePath` 的路径。
    *   创建一个 `FileIndexDetail` 实例。
    *   填充 `FilePath`、`NormalizedFilePath`。
    *   设置 `LastIndexed` 为当前的 `DateTime.UtcNow`。
    *   (可选) 计算并填充 `FileHash`。
    *   将此 `FileIndexDetail` 实例添加到对应 `CodebaseMapping` 的 `FileIndexDetails` 列表中。
*   在索引任务完成时，通过 `IndexConfigManager.UpdateMapping()` 保存包含 `FileIndexDetails` 的 `CodebaseMapping`。

#### 2.2.2. 新的 `RebuildIndexAsync` 逻辑 (增量重建)

此方法将替代原有的全量重建逻辑。

1.  **获取映射和当前文件：**
    *   根据 `codebasePath` 从 `IndexConfigManager` 获取 `CodebaseMapping`。若不存在，则返回错误。
    *   获取代码库目录下所有符合索引条件（如 `*.cs`，排除 `bin`, `obj` 等）的当前物理文件列表，并转换为相对路径列表。
2.  **加载现有文件索引详情：**
    *   从 `CodebaseMapping.FileIndexDetails` 获取已记录的文件索引信息。
3.  **处理已删除文件：**
    *   遍历 `FileIndexDetails` 中的每一条记录。
    *   如果记录中的 `NormalizedFilePath` 在当前物理文件相对路径列表中不存在，则视该文件已被删除。
    *   调用 `_searchService.DeleteFileIndexAsync(normalizedRelativeFilePath, mapping.CollectionName)` (或类似方法) 从 Qdrant 集合中删除该文件的所有索引条目。
    *   从 `FileIndexDetails` 列表中移除该文件的记录。
    *   记录已删除的文件数量。
4.  **处理新增和已修改文件：**
    *   遍历当前物理文件相对路径列表中的每个文件：
        *   在 `FileIndexDetails` 中查找该文件的记录 (基于 `NormalizedFilePath`)。
        *   **新增文件：** 如果记录不存在：
            *   执行单文件索引流程：读取文件、提取代码片段、生成向量、存入 Qdrant。
            *   创建一个新的 `FileIndexDetail` 实例，填充信息（包括新的 `LastIndexed` 时间和可选的 `FileHash`）。
            *   将其添加到 `FileIndexDetails` 列表中。
            *   记录新增的文件数量和索引的片段数量。
        *   **已存在文件（可能已修改）：** 如果记录存在：
            *   获取文件的当前最后修改时间 (`File.GetLastWriteTimeUtc(absoluteFilePath)`)。
            *   (可选) 计算当前文件的哈希值。
            *   **判断是否修改：** 如果 `currentLastWriteTime > detail.LastIndexed` (或者如果启用了哈希比较，`currentFileHash != detail.FileHash`)，则文件已修改。
                *   执行单文件更新索引流程：首先调用 `_searchService.DeleteFileIndexAsync()` 删除该文件旧的索引条目，然后执行单文件索引流程（提取、生成、存储）。
                *   更新 `FileIndexDetails` 中该文件记录的 `LastIndexed` 为当前时间，并更新 `FileHash` (如果使用)。
                *   记录已修改的文件数量和更新的片段数量。
            *   如果文件未修改，则不进行任何操作。
5.  **更新映射和任务状态：**
    *   更新 `CodebaseMapping` 的 `LastIndexed` 和 `Statistics.LastUpdateTime` 为当前时间。
    *   更新 `Statistics` 中的文件总数、索引片段总数等（需要累加新增和减去删除的）。
    *   通过 `IndexConfigManager.UpdateMapping()` 保存对 `CodebaseMapping` (包括 `FileIndexDetails`) 的所有更改到 `codebase-indexes.json`。
    *   更新 `IndexingTask` 的状态为 `Completed` (或 `Failed`)，并填充相关信息 (如处理的文件数、耗时等)。

#### 2.2.3. `UpdateFileIndexAsync` (文件监控触发的更新)

*   当此方法成功处理一个文件的创建、修改事件并更新其在 Qdrant 中的索引后：
    *   查找 `CodebaseMapping.FileIndexDetails` 中对应文件的记录。
    *   如果文件是新建的且记录不存在，则添加新的 `FileIndexDetail`。
    *   更新该记录的 `LastIndexed` 为当前时间，并更新 `FileHash` (如果使用)。
    *   通过 `IndexConfigManager.UpdateMapping()` 保存更改。
*   当处理文件删除事件并成功从 Qdrant 中删除索引后：
    *   从 `FileIndexDetails` 中移除对应文件的记录。
    *   通过 `IndexConfigManager.UpdateMapping()` 保存更改。

### 2.3. `EnhancedCodeSemanticSearch.cs` (或相关服务)

*   **必须** 提供或确保一个可靠的方法，例如 `Task<bool> DeleteFileIndexAsync(string normalizedRelativeFilePath, string collectionName)`。
*   此方法需要能够根据文件的规范化相对路径，从指定的 Qdrant 集合中精确删除所有与该文件相关的代码片段向量。
*   实现方式可能是在 Qdrant 中存储点时，为每个点（代码片段）添加一个元数据字段，如 `source_file_path: "path/to/file.cs"`，然后使用此字段进行过滤删除。

### 2.4. `IndexConfigManager.cs`

*   确保 `LoadConfiguration`、`SaveConfigurationInternal`、`UpdateMapping` 等方法能够正确序列化和反序列化新增的 `FileIndexDetails` 列表及其内容。通常情况下，如果使用了标准的 `System.Text.Json` 且模型定义正确，这部分不需要大的改动，但需要测试验证。

## 3. Mermaid 图 - 更新后的重建索引流程

```mermaid
graph TD
    A[RebuildIndexAsync(codebasePath)] --> B{Get CodebaseMapping};
    B -- Exists --> C[Get current physical files in codebase (relative paths)];
    B -- Not Exists --> X[Return Error: Mapping not found];
    C --> D[Load existing FileIndexDetails from Mapping];
    
    D --> E{Iterate FileIndexDetails (FID)};
    E -- FID.NormalizedFilePath NOT in current physical files? --> F[Mark as Deleted];
    F --> G[Call _searchService.DeleteFileIndexAsync(FID.NormalizedFilePath, collectionName)];
    G --> H[Remove FID from FileIndexDetails list in memory];
    E -- Next FID --> E;
    E -- Done iterating FID --> I;
    
    I{Iterate current physical files (CPF) with relative paths};
    I -- CPF path --> J[Normalize CPF relative path];
    J --> K{Find corresponding FID for J in FileIndexDetails};
    K -- Not Found (New File) --> L[Index new file (read, snippets, vectors, store in Qdrant)];
    L --> M[Create new FileIndexDetail (path, new LastIndexed, new FileHash)];
    M --> N[Add new FID to FileIndexDetails list in memory];
    K -- Found (Existing File) --> O{CPF.LastWriteTime > FID.LastIndexed OR CPF.Hash != FID.FileHash?};
    O -- Yes (Modified File) --> P[Call _searchService.DeleteFileIndexAsync(FID.NormalizedFilePath, collectionName)];
    P --> Q[Index modified file (read, snippets, vectors, store in Qdrant)];
    Q --> R[Update existing FID in memory (new LastIndexed, new FileHash)];
    O -- No (Unchanged) --> I;
    N --> I;
    R --> I;
    I -- Done iterating CPF --> S;
    
    S[Update CodebaseMapping in memory: LastIndexed=now, update Statistics];
    S --> T[Call IndexConfigManager.UpdateMapping(updatedMapping)];
    T --> U[Update IndexingTask status (Completed/Failed, progress, counts)];
    U --> V[Return Success/Result];
```

## 4. 任务分解与实施步骤

1.  **数据模型定义 (1-2小时):**
    *   在 `CodebaseMcpServer/Models/IndexConfiguration.cs` 中定义 `FileIndexDetail` 类。
    *   在 `CodebaseMapping` 类中添加 `FileIndexDetails` 属性。
2.  **`IndexConfigManager` 适应性修改与测试 (1小时):**
    *   验证 `LoadConfiguration` 和 `SaveConfigurationInternal` (以及调用它们的方法如 `UpdateMapping`) 能正确处理 `FileIndexDetails`。
3.  **`EnhancedCodeSemanticSearch` 文件删除能力确认/实现 (2-3小时):**
    *   实现或确认 `DeleteFileIndexAsync(string normalizedRelativeFilePath, string collectionName)` 方法。
    *   确保 Qdrant 点创建时包含可用于精确删除的 `source_file_path` 元数据。
4.  **`IndexingTaskManager.ExecuteIndexingTaskAsync` 修改 (创建新索引时) (2-3小时):**
    *   在完整索引流程成功后，遍历已索引文件，计算相对路径和可选的文件哈希，填充并保存 `CodebaseMapping.FileIndexDetails`。
5.  **`IndexingTaskManager.UpdateFileIndexAsync` 修改 (文件监控触发时) (1.5-2.5小时):**
    *   在成功更新/删除单个文件索引后，同步更新/移除 `FileIndexDetails` 中对应文件的条目（包括 `LastIndexed` 和 `FileHash`）。
6.  **`IndexingTaskManager.RebuildIndexAsync` 核心逻辑重写 (5-7小时):**
    *   实现 Mermaid 图中描述的增量重建逻辑：
        *   处理已删除文件（调用步骤3的方法）。
        *   处理新增文件（复用单文件索引逻辑，更新 `FileIndexDetails`）。
        *   处理已修改文件（先删除旧索引，再进行单文件索引，更新 `FileIndexDetails`）。
    *   确保所有对 `FileIndexDetails` 和 `CodebaseMapping` 的更改都通过 `IndexConfigManager` 持久化。
7.  **工具层 (`IndexManagementTools.cs`) (0.5小时):**
    *   检查 `RebuildIndex` 工具的描述和返回信息，确保其反映增量行为。
8.  **测试 (5-9小时):**
    *   **单元测试：** 各个修改的模块和关键逻辑。
    *   **集成测试：**
        *   创建新索引：验证 `FileIndexDetails` 正确填充。
        *   修改文件后重建：验证仅修改文件被重索引，`FileIndexDetails` 更新。
        *   删除文件后重建：验证已删除文件从索引和 `FileIndexDetails` 中移除。
        *   添加新文件后重建：验证新文件被索引并添加到 `FileIndexDetails`。
        *   无变化代码库重建：验证无索引操作，`FileIndexDetails` 不变。
        *   包含多种变更（增、删、改）的复杂场景重建。
9.  **（可选）文件哈希实现 (1-2小时):**
    *   如果决定实现文件哈希比较，添加哈希计算逻辑 (例如 SHA256) 并在相应位置使用。
10. **错误处理和日志增强 (1-2小时):**
    *   在新的和修改的逻辑中添加清晰、详细的日志记录。
    *   确保稳健的错误处理和状态报告。

**总预估时间：** 约 20 - 32 小时 (不完全包含可选的哈希实现和日志增强的全部时间)

## 5. 注意事项

*   **路径规范化：** 在比较和存储文件路径时，务必使用一致的规范化方法 (例如，统一转换为小写，使用系统的路径分隔符，处理相对/绝对路径转换)。[`PathExtensions.cs`](CodebaseMcpServer/Extensions/PathExtensions.cs) 中的方法可以复用。
*   **并发控制：** `IndexConfigManager` 已经使用了 `SemaphoreSlim` 进行文件访问控制。在 `IndexingTaskManager` 中对 `CodebaseMapping` 对象进行修改时，如果涉及并发场景（例如，文件监控和手动重建同时操作一个代码库），需要确保线程安全。
*   **性能：** 对于非常大的代码库，文件哈希计算可能会增加处理时间。如果选择实现，应考虑其性能影响。初始版本可以不包含哈希，后续再优化加入。
*   **Qdrant 元数据：** 确保向 Qdrant 存储点时，包含 `source_file_path` (或其他唯一标识文件的元数据)，以便 `DeleteFileIndexAsync` 能够精确工作。
*   **事务性/回滚：** 当前设计未包含复杂的事务回滚机制。如果重建过程中部分失败，`codebase-indexes.json` 可能处于中间状态。需要考虑这种情况下的健壮性。对于关键操作，可以考虑先备份配置文件。