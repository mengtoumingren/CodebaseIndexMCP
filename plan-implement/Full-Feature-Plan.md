# 全功能计划：索引库编辑功能

**目标**: 在 `index.html` 管理控制台中，为索引库增加编辑功能，并优化 UI 布局。

**核心需求**:
1.  **UI 整合**: 将“创建索引库”功能移入“索引库管理”标签页。
2.  **编辑功能**: 允许用户编辑现有索引库的**名称**和**关联的配置预设**。

---

## 任务分解

此任务将分解为两个主要部分：后端增强和前端实现。

### 1. 后端增强 (Phase 1)

**目标**: 创建一个新的 API 端点，以支持更新索引库的关联配置预设。

**步骤**:
1.  **定义请求模型**: 在 `CodebaseMcpServer/Models/Domain` 目录下创建一个新的 `UpdateLibraryPresetsRequest.cs` 文件。
2.  **扩展服务层接口**: 在 `IIndexLibraryService.cs` 中添加一个新的方法签名：`Task<bool> UpdatePresetsAsync(int libraryId, List<string> presetIds);`
3.  **实现服务层逻辑**: 在 `IndexLibraryService.cs` 中实现 `UpdatePresetsAsync` 方法。
4.  **创建控制器端点**: 在 `IndexLibraryController.cs` 中添加一个新的 `HTTP PUT` 端点 `[HttpPut("{id}/presets")]`。

### 2. 前端实现 (Phase 2)

**目标**: 在 `index.html` 中实现 UI 更改和功能逻辑。

**步骤**:
1.  **HTML 结构调整**:
    *   移除主导航栏中的“创建索引库”标签。
    *   在“索引库管理”部分添加一个“创建新索引库”按钮。
    *   将创建表单移入一个可复用的模态框中。
2.  **JavaScript 逻辑增强**:
    *   创建一个通用的模态框函数 `showLibraryModal(libraryId = null)` 用于创建和编辑。
    *   在 `loadLibraries()` 中为每个库动态生成“编辑”按钮。
    *   创建一个 `saveLibrary()` 函数来处理模态框的提交，根据是创建还是编辑来调用不同的 API。
    *   确保预设加载逻辑可被编辑模态框复用。

---

## 工作流程图

```mermaid
graph TD
    subgraph "索引库管理页面"
        A[用户访问页面] --> B(查看索引库列表);
        B --> C[点击 "➕ 创建新索引库" 按钮];
        B --> D[点击某个索引库的 "✏️ 编辑" 按钮];
    end

    subgraph "创建/编辑模态框"
        C --> E[打开一个空的表单模态框];
        D --> F[获取该索引库的现有数据];
        F --> G[打开填充了数据的表单模态框];
        E --> H{填写/修改表单信息 (名称和预设)};
        G --> H;
        H --> I[点击 "保存" 按钮];
    end

    subgraph "后台交互"
        I -- 创建 --> J_Create[POST /api/IndexLibrary];
        I -- 编辑 --> J_UpdateName[PUT /api/IndexLibrary/{id}];
        J_UpdateName --> J_UpdatePresets[PUT /api/IndexLibrary/{id}/presets];
        J_Create -- 成功 --> K[关闭模态框 & 刷新列表];
        J_UpdatePresets -- 成功 --> K;
        J_Create -- 失败 --> L[显示错误提示];
        J_UpdateName -- 失败 --> L;
        J_UpdatePresets -- 失败 --> L;
    end
    
```