# IndexingTaskManager 嵌入模型并发调用流程图

## 1. 当前串行处理流程

```mermaid
graph TD
    A[开始索引任务] --> B[扫描文件列表]
    B --> C[创建文件批次]
    C --> D[处理批次1]
    D --> E[解析文件1]
    E --> F[解析文件2]
    F --> G[解析文件N]
    G --> H[提取所有代码片段]
    H --> I[串行调用嵌入API]
    I --> J[批次1完成]
    J --> K[处理批次2]
    K --> L[...]
    L --> M[所有批次完成]
    
    subgraph "串行嵌入处理"
        I --> I1[文本1 → API调用]
        I1 --> I2[文本2 → API调用]
        I2 --> I3[文本N → API调用]
    end
    
    style I fill:#ffcccc
    style I1 fill:#ffcccc
    style I2 fill:#ffcccc
    style I3 fill:#ffcccc
```

## 2. 改进后并发处理流程

```mermaid
graph TD
    A[开始索引任务] --> B[扫描文件列表]
    B --> C[创建文件批次]
    C --> D[并发处理多个批次]
    
    D --> E1[批次1并发处理]
    D --> E2[批次2并发处理]  
    D --> E3[批次N并发处理]
    
    E1 --> F1[并发解析文件]
    E2 --> F2[并发解析文件]
    E3 --> F3[并发解析文件]
    
    F1 --> G1[并发嵌入向量获取]
    F2 --> G2[并发嵌入向量获取]
    F3 --> G3[并发嵌入向量获取]
    
    G1 --> H1[批次1索引完成]
    G2 --> H2[批次2索引完成]
    G3 --> H3[批次N索引完成]
    
    H1 --> I[合并所有结果]
    H2 --> I
    H3 --> I
    I --> J[索引任务完成]
    
    subgraph "并发嵌入处理详细"
        G1 --> G1A[文本批次A → 并发API调用]
        G1 --> G1B[文本批次B → 并发API调用]
        G1 --> G1C[文本批次C → 并发API调用]
        G1A --> G1D[结果合并]
        G1B --> G1D
        G1C --> G1D
    end
    
    style D fill:#ccffcc
    style E1 fill:#ccffcc
    style E2 fill:#ccffcc
    style E3 fill:#ccffcc
    style G1 fill:#ccffcc
    style G2 fill:#ccffcc
    style G3 fill:#ccffcc
```

## 3. 并发架构层次图

```mermaid
graph TB
    subgraph "应用层 (Application Layer)"
        A1[IndexingTaskManager]
        A2[文件批次调度器]
    end
    
    subgraph "并发管理层 (Concurrency Management Layer)"
        B1[ConcurrentEmbeddingManager]
        B2[SemaphoreSlim 并发控制]
        B3[智能批次分割器]
        B4[重试和错误处理]
    end
    
    subgraph "嵌入服务层 (Embedding Service Layer)"
        C1[EnhancedCodeSemanticSearch]
        C2[IEmbeddingProvider 抽象]
        C3[并发 HTTP 请求池]
    end
    
    subgraph "提供商实现层 (Provider Implementation Layer)"
        D1[DashScopeEmbeddingProvider]
        D2[OllamaEmbeddingProvider]
        D3[OpenAIEmbeddingProvider]
        D4[其他提供商...]
    end
    
    subgraph "网络和存储层 (Network & Storage Layer)"
        E1[HTTP Client Factory]
        E2[Qdrant 向量数据库]
        E3[本地文件系统]
    end
    
    A1 --> B1
    A2 --> B1
    B1 --> B2
    B1 --> B3
    B1 --> B4
    B1 --> C1
    C1 --> C2
    C2 --> C3
    C3 --> D1
    C3 --> D2
    C3 --> D3
    C3 --> D4
    D1 --> E1
    D2 --> E1
    D3 --> E1
    C1 --> E2
    A1 --> E3
```

## 4. 并发嵌入向量处理时序图

```mermaid
sequenceDiagram
    participant TM as IndexingTaskManager
    participant CEM as ConcurrentEmbeddingManager
    participant SL as SemaphoreSlim限制器
    participant EP as EmbeddingProvider
    participant API as 嵌入向量API
    
    TM->>CEM: 请求处理100个文本
    CEM->>CEM: 智能分割为4个批次
    
    par 并发批次1
        CEM->>SL: 请求并发许可
        SL-->>CEM: 许可获得
        CEM->>EP: 批次1 (25个文本)
        EP->>API: HTTP请求
        API-->>EP: 嵌入向量响应
        EP-->>CEM: 批次1结果
        CEM->>SL: 释放许可
    and 并发批次2
        CEM->>SL: 请求并发许可
        SL-->>CEM: 许可获得
        CEM->>EP: 批次2 (25个文本)
        EP->>API: HTTP请求
        API-->>EP: 嵌入向量响应
        EP-->>CEM: 批次2结果
        CEM->>SL: 释放许可
    and 并发批次3
        CEM->>SL: 请求并发许可
        SL-->>CEM: 许可获得
        CEM->>EP: 批次3 (25个文本)
        EP->>API: HTTP请求
        API-->>EP: 嵌入向量响应
        EP-->>CEM: 批次3结果
        CEM->>SL: 释放许可
    and 并发批次4
        CEM->>SL: 请求并发许可
        SL-->>CEM: 许可获得
        CEM->>EP: 批次4 (25个文本)
        EP->>API: HTTP请求
        API-->>EP: 嵌入向量响应
        EP-->>CEM: 批次4结果
        CEM->>SL: 释放许可
    end
    
    CEM->>CEM: 合并所有批次结果
    CEM-->>TM: 返回100个嵌入向量
```

## 5. 错误处理和重试机制流程

```mermaid
graph TD
    A[批次处理开始] --> B[尝试嵌入向量获取]
    B --> C{是否成功?}
    C -->|成功| D[返回结果]
    C -->|失败| E[记录错误日志]
    E --> F{达到最大重试次数?}
    F -->|否| G[指数退避延迟]
    G --> H[重试次数+1]
    H --> B
    F -->|是| I{启用失败回退?}
    I -->|是| J[返回零向量]
    I -->|否| K[抛出异常]
    J --> L[记录回退日志]
    L --> M[继续处理下一批次]
    K --> N[任务失败]
    
    style E fill:#ffcccc
    style K fill:#ff9999
    style J fill:#ffffcc
    style D fill:#ccffcc
```

## 6. 性能对比分析图

```mermaid
graph LR
    subgraph "串行处理 (当前)"
        A1[文件1: 2秒] --> A2[文件2: 2秒]
        A2 --> A3[文件3: 2秒]
        A3 --> A4[文件4: 2秒]
        A4 --> A5[总计: 8秒]
    end
    
    subgraph "并发处理 (改进后)"
        B1[文件1: 2秒]
        B2[文件2: 2秒]
        B3[文件3: 2秒]
        B4[文件4: 2秒]
        B1 --> B5[总计: 2秒]
        B2 --> B5
        B3 --> B5
        B4 --> B5
    end
    
    subgraph "性能提升"
        C1[时间节省: 75%]
        C2[CPU利用率: 60-80%]
        C3[并发度: 4x]
    end
    
    style A5 fill:#ffcccc
    style B5 fill:#ccffcc
    style C1 fill:#ccffcc
    style C2 fill:#ccffcc
    style C3 fill:#ccffcc
```

## 7. 配置参数影响分析

```mermaid
graph TB
    subgraph "并发配置参数"
        A1[MaxConcurrentEmbeddingRequests]
        A2[MaxConcurrentFileBatches]
        A3[EmbeddingBatchSizeOptimal]
        A4[NetworkTimeoutMs]
    end
    
    subgraph "性能影响"
        B1[处理速度]
        B2[内存使用]
        B3[网络负载]
        B4[错误率]
    end
    
    subgraph "硬件资源"
        C1[CPU核心数]
        C2[内存大小]
        C3[网络带宽]
        C4[API限制]
    end
    
    A1 --> B1
    A1 --> B3
    A2 --> B1
    A2 --> B2
    A3 --> B2
    A3 --> B3
    A4 --> B4
    
    C1 --> A2
    C2 --> A3
    C3 --> A1
    C4 --> A1
    
    style A1 fill:#e1f5fe
    style A2 fill:#e1f5fe
    style A3 fill:#e1f5fe
    style A4 fill:#e1f5fe
```

## 8. 内存使用优化对比

```mermaid
graph TD
    subgraph "改进前内存使用"
        A1[加载所有文件] --> A2[解析所有代码片段]
        A2 --> A3[内存中保存所有片段]
        A3 --> A4[批量获取嵌入向量]
        A4 --> A5[峰值内存: O(n)]
    end
    
    subgraph "改进后内存使用"
        B1[分批加载文件] --> B2[分批解析代码片段]
        B2 --> B3[及时释放已处理片段]
        B3 --> B4[并发获取嵌入向量]
        B4 --> B5[峰值内存: O(batch_size)]
    end
    
    subgraph "内存效益"
        C1[内存使用减少: 70-80%]
        C2[GC压力降低]
        C3[大型项目支持改善]
    end
    
    A5 --> C1
    B5 --> C1
    
    style A5 fill:#ffcccc
    style B5 fill:#ccffcc
    style C1 fill:#ccffcc
    style C2 fill:#ccffcc
    style C3 fill:#ccffcc
```

## 9. 实际使用场景流程

### 场景1：大型代码库初始索引

```mermaid
graph TD
    A[用户启动索引: 5000个文件] --> B[系统自动检测并发配置]
    B --> C[创建20个文件批次]
    C --> D[并发处理4个批次]
    D --> E[每个批次并发处理文件]
    E --> F[智能分割嵌入向量请求]
    F --> G[并发调用API获取向量]
    G --> H[实时更新进度显示]
    H --> I[批次完成后立即释放内存]
    I --> J[所有批次完成]
    J --> K[索引时间: 30分钟 (原60分钟)]
    
    style K fill:#ccffcc
```

### 场景2：增量重建优化

```mermaid
graph TD
    A[检测到100个文件变更] --> B[并发分析文件状态]
    B --> C[识别出30个需要重新索引]
    C --> D[并发处理文件更新]
    D --> E[每个文件并发获取嵌入向量]
    E --> F[并发更新Qdrant索引]
    F --> G[并发更新元数据]
    G --> H[增量重建完成: 3分钟 (原10分钟)]
    
    style H fill:#ccffcc
```

这个流程图全面展示了嵌入模型并发调用的改进方案，包括架构设计、性能对比、错误处理和实际应用场景，为实施提供了清晰的技术指导。