# CodebaseMcpServer 整洁架构 (Clean Architecture) 重构计划

**最后更新时间**: 2025-06-26

**作者**: Cline

## 1. 目标与动机

### 1.1. 当前问题

随着 `CodebaseMcpServer` 项目的功能日益复杂，当前基于文件夹分类的扁平化项目结构已成为维护和扩展的瓶颈。主要问题包括：

*   **职责混淆**: `Services` 文件夹下混合了业务逻辑、数据访问、外部API调用等多种职责。
*   **高耦合**: 业务逻辑与基础设施（如数据库实现、文件系统）紧密耦合。
*   **可测试性差**: 核心业务逻辑难以进行独立的单元测试。
*   **扩展性受限**: 难以清晰地添加新的功能模块。

### 1.2. 优化目标

本次重构旨在**在现有解决方案中引入整洁架构**，通过创建一个新的表现层 `CodebaseMcpServer_v2` 并将逻辑分层，实现以下目标，同时**保留原始 `CodebaseMcpServer` 项目作为参照**，确保重构的正确性。

*   **关注点分离 (SoC)**: 将项目明确划分为 **Domain, Application, Infrastructure, Web** 四层。
*   **依赖倒置**: 核心业务逻辑不依赖于具体的技术实现。
*   **提升可维护性与可扩展性**: 使代码结构清晰，易于理解、修改和扩展。
*   **增强可测试性**: 使核心业务逻辑可以被轻松地进行单元测试。

## 2. 建议的新架构

我们将创建一个新的Web API项目 `CodebaseMcpServer_v2` 作为表现层，并为其创建独立的分层项目。

```mermaid
graph TD
    subgraph Solution: CoodeBaseApp.sln
        A[CodebaseMcpServer_v2 (Web)] --> B[CodebaseMcpServer.Application]
        B --> C[CodebaseMcpServer.Domain]
        D[CodebaseMcpServer.Infrastructure] --> B
        A --> D
        E[CodebaseMcpServer (Original, For Reference)]
    end

    style A fill:#D6EAF8,stroke:#3498DB,stroke-width:2px
    style B fill:#D1F2EB,stroke:#1ABC9C,stroke-width:2px
    style C fill:#FEF9E7,stroke:#F1C40F,stroke-width:2px
    style D fill:#FADBD8,stroke:#E74C3C,stroke-width:2px
    style E fill:#F5F5F5,stroke:#BDBDBD,stroke-width:2px
```

*   **Domain**: 核心领域层，包含实体、值对象、领域事件和仓储接口。
*   **Application**: 应用层，包含应用服务、DTOs、命令/查询，负责编排业务流程。
*   **Infrastructure**: 基础设施层，实现仓储接口、与数据库/文件系统/外部API交互。
*   **Web (Host)**: 表现层 (`CodebaseMcpServer_v2`)，包含Controllers (API), Tools (MCP), 和 `Program.cs`。

## 3. 分阶段实施计划

**总预估时间**: 13-20 小时

---

### **阶段一：创建新项目骨架 (预计时间: 1-2小时)**

**目标**: 在现有解决方案中创建新的分层项目和新的表现层项目。

1.  **使用现有解决方案**: 我们将直接在 `CoodeBaseApp.sln` 中进行操作。
2.  **创建分层项目**: 在解决方案中创建三个新的 .NET 类库项目：
    *   `CodebaseMcpServer.Domain`
    *   `CodebaseMcpServer.Application`
    *   `CodebaseMcpServer.Infrastructure`
3.  **创建新表现层**: 创建一个新的 ASP.NET Core Web API 项目：
    *   `CodebaseMcpServer_v2`
4.  **设置项目引用**:
    *   `CodebaseMcpServer.Application` 引用 `CodebaseMcpServer.Domain`。
    *   `CodebaseMcpServer.Infrastructure` 引用 `CodebaseMcpServer.Application`。
    *   `CodebaseMcpServer_v2` 引用 `CodebaseMcpServer.Application` 和 `CodebaseMcpServer.Infrastructure`。

---

### **阶段二：迁移核心领域层 (Domain) (预计时间: 2-3小时)**

**目标**: 将原始项目中最核心的业务模型和规则迁移到 `CodebaseMcpServer.Domain`。

1.  **迁移实体和值对象**: 将原始 `CodebaseMcpServer` 项目中 `Models/Domain` 和其他核心模型文件**复制**到 `CodebaseMcpServer.Domain` 项目中。
2.  **定义仓储接口**: 在 `CodebaseMcpServer.Domain` 项目中为每个聚合根创建仓储接口 (Repository Interfaces)。

---

### **阶段三：迁移基础设施层 (Infrastructure) (预计时间: 4-6小时)**

**目标**: 将原始项目所有与外部技术相关的实现细节迁移到 `CodebaseMcpServer.Infrastructure`。

1.  **迁移数据持久化**: 将原始 `CodebaseMcpServer` 中仓储的实现、数据库上下文等代码**复制并重构**到 `CodebaseMcpServer.Infrastructure`，并使其继承 `Domain` 层定义的接口。
2.  **迁移外部服务**: 将与第三方API和操作系统交互的代码**复制并重构**到 `CodebaseMcpServer.Infrastructure`。
3.  **添加依赖**: 为 `CodebaseMcpServer.Infrastructure` 添加所有必需的NuGet包。

---

### **阶段四：迁移应用层 (Application) (预计时间: 4-6小时)**

**目标**: 构建新应用的业务用例，连接表现层和领域层。

1.  **创建应用服务**: 在 `CodebaseMcpServer.Application` 中创建新的应用服务。
2.  **迁移业务逻辑**: 将原始 `CodebaseMcpServer` 中 `Services` 层的业务编排逻辑**迁移并重构**到新的应用服务中。
3.  **定义DTOs和命令**: 创建数据传输对象 (DTOs) 和命令 (Commands) 用于与表现层交互。
4.  **定义服务接口**: 在 `Application` 层为应用服务创建接口，供表现层依赖。

---

### **阶段五：构建新表现层 (Web/Host) (预计时间: 2-3小时)**

**目标**: 让 `CodebaseMcpServer_v2` 项目变“薄”，只负责请求的接收、转发和依赖注入。

1.  **更新依赖注入**: 在 `CodebaseMcpServer_v2` 的 `Program.cs` 中注册所有 `Application` 和 `Infrastructure` 层的服务。
2.  **迁移Controllers和Tools**: 将原始 `CodebaseMcpServer` 中的 `Controllers` 和 `Tools` **复制并重构**到 `CodebaseMcpServer_v2`，使其调用 `Application` 层的服务接口。
3.  **使用DTOs**: 确保 `Controllers` 和 `Tools` 使用 `Application` 层定义的DTOs。
4.  **最终清理**: 确保 `CodebaseMcpServer_v2` 具备原始项目的所有功能，而原始 `CodebaseMcpServer` 保持不变。
