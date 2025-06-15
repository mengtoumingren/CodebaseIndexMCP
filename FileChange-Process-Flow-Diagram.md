# 文件变更处理流程图

## 改进前的流程

```mermaid
flowchart TD
    A[文件变更检测] --> B{过滤检查}
    B -->|通过| C[添加到内存队列]
    B -->|不通过| Z[忽略]
    C --> D[定时批处理]
    D --> E[处理变更]
    E --> F[更新索引]
    F -->|成功| G[完成]
    F -->|失败| H[丢失]
    
    %% 服务中断风险点
    X[服务中断] -.->|导致| H
    X -.->|影响| C
    X -.->|影响| D
    
    style H fill:#ff9999
    style X fill:#ffcccc
```

## 改进后的流程

```mermaid
flowchart TD
    A[文件变更检测] --> B{过滤检查}
    B -->|通过| C[创建变更事件]
    B -->|不通过| Z[忽略]
    
    C --> D[🔥 立即持久化到本地]
    D -->|成功| E[状态: Pending]
    D -->|失败| F[降级到内存队列]
    
    E --> G[定时批处理扫描]
    F --> G
    G --> H[加载待处理变更]
    H --> I{有变更?}
    I -->|是| J[去重和分组]
    I -->|否| G
    
    J --> K[处理单个变更]
    K --> L[🔥 标记为 Processing]
    L --> M[更新索引]
    
    M -->|成功| N[标记为 Completed]
    M -->|失败| O[标记为 Failed + 重试计数]
    
    N --> P[🔥 删除持久化记录]
    O --> Q{可重试?}
    Q -->|是| R[等待重试]
    Q -->|否| S[标记为过期]
    
    R --> G
    P --> T[完成]
    
    %% 服务重启恢复流程
    START[服务启动] --> RECOVER[🔥 恢复未完成变更]
    RECOVER --> CHECK1{有 Processing 状态?}
    CHECK1 -->|是| RESET[重置为 Pending]
    CHECK1 -->|否| CHECK2{有 Pending 状态?}
    RESET --> CHECK2
    CHECK2 -->|是| TRIGGER[触发立即处理]
    CHECK2 -->|否| NORMAL[正常启动]
    TRIGGER --> G
    
    %% 定期清理
    CLEANUP[定期清理任务] --> CLEAN1[清理过期记录]
    CLEAN1 --> CLEAN2[清理完成记录]
    
    style D fill:#90EE90
    style L fill:#FFE4B5
    style N fill:#90EE90
    style P fill:#90EE90
    style RECOVER fill:#87CEEB
    style RESET fill:#87CEEB
    style TRIGGER fill:#87CEEB
```

## 关键改进点

### 1. 持久化优先 (🔥 核心改进)
```mermaid
flowchart LR
    A[检测到变更] --> B[立即持久化]
    B --> C[异步处理]
    C --> D[处理完成]
    D --> E[删除记录]
    
    style B fill:#90EE90
    style E fill:#90EE90
```

### 2. 状态生命周期管理
```mermaid
stateDiagram-v2
    [*] --> Pending : 变更检测
    Pending --> Processing : 开始处理
    Processing --> Completed : 处理成功
    Processing --> Failed : 处理失败
    Failed --> Pending : 重试
    Failed --> Expired : 超过重试次数
    Completed --> [*] : 删除记录
    Expired --> [*] : 定期清理
```

### 3. 服务重启恢复机制
```mermaid
flowchart TD
    A[服务启动] --> B[扫描持久化目录]
    B --> C{发现未完成变更?}
    C -->|否| D[正常启动]
    C -->|是| E[加载变更记录]
    E --> F{Processing 状态?}
    F -->|是| G[重置为 Pending]
    F -->|否| H[保持原状态]
    G --> I[触发立即处理]
    H --> I
    I --> J[恢复完成]
    
    style E fill:#87CEEB
    style G fill:#87CEEB
    style I fill:#87CEEB
```

## 错误处理和重试机制

```mermaid
flowchart TD
    A[处理变更] --> B{处理成功?}
    B -->|是| C[删除持久化记录]
    B -->|否| D[记录错误信息]
    D --> E{重试次数 < 最大值?}
    E -->|是| F[增加重试计数]
    E -->|否| G[标记为过期]
    F --> H[等待重试间隔]
    H --> I[重新处理]
    I --> A
    G --> J[定期清理]
    
    style D fill:#FFB6C1
    style G fill:#FFB6C1
```

## 并发安全处理

```mermaid
flowchart LR
    A[多个文件变更] --> B[文件锁保护]
    B --> C[依次持久化]
    C --> D[批处理去重]
    D --> E[并发处理]
    E --> F[状态同步更新]
    
    style B fill:#DDA0DD
    style F fill:#DDA0DD
```

## 性能优化策略

```mermaid
flowchart TD
    A[文件变更检测] --> B[异步持久化]
    B --> C[批量处理]
    C --> D[去重优化]
    D --> E[并发执行]
    E --> F[批量状态更新]
    
    subgraph "性能优化点"
        B
        C  
        D
        E
        F
    end
    
    style B fill:#98FB98
    style C fill:#98FB98
    style D fill:#98FB98
    style E fill:#98FB98
    style F fill:#98FB98
```

## 故障恢复能力对比

### 改进前
```mermaid
graph LR
    A[正常运行] --> B[服务中断]
    B --> C[变更丢失]
    C --> D[手动重建索引]
    
    style C fill:#ff9999
    style D fill:#ffcccc
```

### 改进后  
```mermaid
graph LR
    A[正常运行] --> B[服务中断]
    B --> C[变更已持久化]
    C --> D[服务重启]
    D --> E[自动恢复处理]
    E --> F[无数据丢失]
    
    style C fill:#90EE90
    style E fill:#90EE90
    style F fill:#90EE90
```

## 监控和可观测性

```mermaid
flowchart TD
    A[文件变更事件] --> B[持久化记录]
    B --> C[状态追踪]
    C --> D[处理统计]
    D --> E[错误监控]
    E --> F[性能指标]
    
    subgraph "监控指标"
        G[变更处理成功率]
        H[平均处理时间] 
        I[重试次数统计]
        J[持久化记录数量]
        K[错误类型分布]
    end
    
    F --> G
    F --> H
    F --> I
    F --> J
    F --> K
    
    style F fill:#FFD700
```

---

## 流程说明

### 主要改进
1. **持久化优先**：变更检测后立即保存到本地，确保不丢失
2. **状态管理**：完整的变更处理生命周期跟踪
3. **断点续传**：服务重启后自动恢复未完成处理
4. **错误恢复**：完善的重试和错误处理机制
5. **性能优化**：异步处理、批量操作、并发安全

### 关键特性
- ✅ **零丢失保证**：所有变更都有持久化备份
- ✅ **自动恢复**：服务重启无缝恢复处理
- ✅ **状态可见**：完整的处理状态追踪
- ✅ **错误自愈**：自动重试和故障恢复
- ✅ **运维友好**：丰富的监控和诊断信息

*此流程图配合 FileChange-Persistence-Upgrade-Plan.md 使用，提供可视化的改进方案说明。*