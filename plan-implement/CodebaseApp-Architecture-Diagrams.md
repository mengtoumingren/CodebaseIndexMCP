# CodebaseApp 升级架构图表设计

## 🏗️ 整体系统架构图

```mermaid
graph TB
    subgraph "🌐 用户接口层"
        WEB[Web管理界面<br/>React/Vue + Bootstrap]
        MCP[MCP客户端<br/>Claude/Cursor等]
        API_DOC[API文档<br/>Swagger/OpenAPI]
    end
    
    subgraph "🔌 接口层"
        REST[REST API<br/>ASP.NET Core Controllers]
        MCP_SERVER[MCP服务器<br/>现有MCP工具]
        SIGNALR[SignalR Hub<br/>实时通信]
    end
    
    subgraph "💼 业务逻辑层"
        IDX_SVC[索引库服务<br/>IndexLibraryService]
        WATCH_SVC[文件监视服务<br/>FileWatchService]
        TASK_SVC[后台任务服务<br/>BackgroundTaskService]
        CONFIG_SVC[配置管理服务<br/>ConfigurationService]
    end
    
    subgraph "🔧 基础设施层"
        EMBED[嵌入向量层<br/>多提供商支持]
        PARSER[代码解析层<br/>多语言解析器]
        MONITOR[系统监控<br/>健康检查/指标]
    end
    
    subgraph "💾 数据层"
        SQLITE[(SQLite数据库<br/>配置/任务/事件)]
        QDRANT[(Qdrant向量库<br/>代码向量存储)]
        FILES[文件系统<br/>代码库文件]
    end
    
    %% 接口层连接
    WEB --> REST
    MCP --> MCP_SERVER
    REST --> SIGNALR
    
    %% 业务逻辑层连接
    REST --> IDX_SVC
    REST --> WATCH_SVC
    REST --> TASK_SVC
    REST --> CONFIG_SVC
    
    MCP_SERVER --> IDX_SVC
    MCP_SERVER --> WATCH_SVC
    MCP_SERVER --> TASK_SVC
    
    %% 基础设施连接
    IDX_SVC --> EMBED
    IDX_SVC --> PARSER
    TASK_SVC --> MONITOR
    
    %% 数据层连接
    IDX_SVC --> SQLITE
    IDX_SVC --> QDRANT
    WATCH_SVC --> SQLITE
    TASK_SVC --> SQLITE
    PARSER --> FILES
    
    %% 样式
    classDef interface fill:#e1f5fe
    classDef business fill:#f3e5f5
    classDef infrastructure fill:#fff3e0
    classDef data fill:#e8f5e8
    
    class WEB,MCP,API_DOC,REST,MCP_SERVER,SIGNALR interface
    class IDX_SVC,WATCH_SVC,TASK_SVC,CONFIG_SVC business
    class EMBED,PARSER,MONITOR infrastructure
    class SQLITE,QDRANT,FILES data
```

## 📊 数据库关系图

```mermaid
erDiagram
    IndexLibraries ||--o{ WatchConfigurations : "has"
    IndexLibraries ||--o{ FileIndexDetails : "contains"
    IndexLibraries ||--o{ BackgroundTasks : "processes"
    IndexLibraries ||--o{ FileChangeEvents : "monitors"
    
    IndexLibraries {
        int Id PK
        varchar Name
        varchar CodebasePath UK
        varchar CollectionName UK
        varchar Status
        datetime CreatedAt
        datetime UpdatedAt
        datetime LastIndexedAt
        int TotalFiles
        int IndexedSnippets
        int LastIndexingDuration
        boolean IsActive
    }
    
    WatchConfigurations {
        int Id PK
        int LibraryId FK
        text FilePatterns
        text ExcludePatterns
        boolean IncludeSubdirectories
        boolean IsEnabled
        datetime CreatedAt
        datetime UpdatedAt
    }
    
    FileIndexDetails {
        int Id PK
        int LibraryId FK
        varchar RelativeFilePath
        datetime LastIndexedAt
        int FileSize
        varchar FileHash
        int SnippetCount
        datetime CreatedAt
        datetime UpdatedAt
    }
    
    BackgroundTasks {
        int Id PK
        varchar TaskId UK
        varchar Type
        int LibraryId FK
        varchar Status
        int Progress
        varchar CurrentFile
        text ErrorMessage
        datetime StartedAt
        datetime CompletedAt
        datetime CreatedAt
        datetime UpdatedAt
    }
    
    FileChangeEvents {
        int Id PK
        varchar EventId UK
        int LibraryId FK
        varchar FilePath
        varchar ChangeType
        varchar Status
        datetime ProcessedAt
        text ErrorMessage
        int RetryCount
        datetime CreatedAt
        datetime UpdatedAt
    }
    
    SystemConfigurations {
        int Id PK
        varchar ConfigKey UK
        text ConfigValue
        varchar ConfigType
        text Description
        boolean IsEditable
        datetime CreatedAt
        datetime UpdatedAt
    }
```

## 🔄 业务流程图

### 索引库创建流程

```mermaid
sequenceDiagram
    participant U as 用户
    participant W as Web界面
    participant API as REST API
    participant IDX as 索引库服务
    participant DET as 项目检测器
    participant DB as SQLite数据库
    participant TSK as 后台任务服务
    participant QDR as Qdrant
    
    U->>W: 创建新索引库
    W->>API: POST /api/libraries
    API->>IDX: CreateAsync(request)
    
    IDX->>DET: 检测项目类型
    DET-->>IDX: 返回推荐配置
    
    IDX->>DB: 保存索引库配置
    IDX->>DB: 保存监控配置
    
    IDX->>TSK: 排队索引任务
    TSK->>DB: 保存任务记录
    
    TSK->>QDR: 创建向量集合
    TSK->>TSK: 开始索引处理
    
    loop 文件批处理
        TSK->>TSK: 解析代码文件
        TSK->>QDR: 批量索引向量
        TSK->>DB: 更新进度状态
    end
    
    TSK->>DB: 更新完成状态
    TSK->>W: 推送完成通知(SignalR)
    W-->>U: 显示完成状态
```

### 文件监控和更新流程

```mermaid
sequenceDiagram
    participant FS as 文件系统
    participant FSW as FileWatcher
    participant WS as 监视服务
    participant DB as SQLite数据库
    participant TSK as 后台任务服务
    participant QDR as Qdrant
    
    FS->>FSW: 文件变更事件
    FSW->>WS: OnFileChanged
    
    alt 文件类型匹配
        WS->>DB: 保存变更事件
        WS->>TSK: 排队文件更新任务
        
        TSK->>DB: 标记事件为处理中
        
        alt 文件删除
            TSK->>QDR: 删除相关向量
            TSK->>DB: 删除文件索引记录
        else 文件创建/修改
            TSK->>QDR: 删除旧向量(如存在)
            TSK->>TSK: 解析代码片段
            TSK->>QDR: 索引新向量
            TSK->>DB: 更新文件索引记录
        end
        
        TSK->>DB: 标记事件为已完成
        TSK->>DB: 清理已处理事件
    else 文件类型不匹配
        WS->>WS: 忽略事件
    end
```

## 🌐 Web界面结构图

```mermaid
graph TB
    subgraph "📱 Web管理界面"
        DASH[🏠 仪表板<br/>Dashboard]
        LIB[📚 索引库管理<br/>Libraries]
        TASK[⚙️ 任务监控<br/>Tasks]
        CONF[🔧 系统配置<br/>Configuration]
        MON[📊 监控中心<br/>Monitoring]
    end
    
    subgraph "📊 仪表板组件"
        DASH --> STATS[统计卡片]
        DASH --> CHART[图表展示]
        DASH --> RECENT[最近活动]
        DASH --> ALERTS[系统警告]
    end
    
    subgraph "📚 索引库组件"
        LIB --> LIB_LIST[库列表]
        LIB --> LIB_CREATE[创建向导]
        LIB --> LIB_DETAIL[详情视图]
        LIB --> LIB_CONFIG[配置编辑]
    end
    
    subgraph "⚙️ 任务组件"
        TASK --> TASK_LIST[任务列表]
        TASK --> TASK_PROG[进度监控]
        TASK --> TASK_LOG[日志查看]
        TASK --> TASK_CTRL[任务控制]
    end
    
    subgraph "🔧 配置组件"
        CONF --> SYS_CONF[系统设置]
        CONF --> FILE_CONF[文件类型配置]
        CONF --> EMBED_CONF[嵌入向量配置]
        CONF --> WATCH_CONF[监控配置]
    end
    
    subgraph "📊 监控组件"
        MON --> REAL_TIME[实时状态]
        MON --> PERF_MON[性能监控]
        MON --> ERR_LOG[错误日志]
        MON --> HEALTH[健康检查]
    end
```

## 🚀 部署架构图

```mermaid
graph TB
    subgraph "🌐 客户端"
        BROWSER[Web浏览器]
        MCP_CLIENT[MCP客户端<br/>Claude/Cursor]
    end
    
    subgraph "🖥️ 应用服务器"
        subgraph "ASP.NET Core应用"
            WEB_HOST[Web主机]
            MCP_HOST[MCP主机]
            BG_HOST[后台服务主机]
        end
        
        subgraph "静态资源"
            STATIC[wwwroot/静态文件]
        end
    end
    
    subgraph "💾 数据存储"
        SQLITE_DB[(SQLite数据库<br/>配置/任务/事件)]
        QDRANT_DB[(Qdrant向量数据库<br/>代码向量)]
        FILE_SYS[文件系统<br/>代码库文件]
    end
    
    subgraph "🔧 外部服务"
        EMBED_API[嵌入向量API<br/>OpenAI/DashScope/Ollama]
    end
    
    %% 连接关系
    BROWSER -.HTTP/HTTPS.-> WEB_HOST
    MCP_CLIENT -.Stdio/SSE.-> MCP_HOST
    
    WEB_HOST --> SQLITE_DB
    MCP_HOST --> SQLITE_DB
    BG_HOST --> SQLITE_DB
    
    WEB_HOST --> QDRANT_DB
    MCP_HOST --> QDRANT_DB
    BG_HOST --> QDRANT_DB
    
    BG_HOST --> FILE_SYS
    BG_HOST -.HTTP.-> EMBED_API
    
    WEB_HOST --> STATIC
```

## 📦 模块依赖图

```mermaid
graph TD
    subgraph "🔌 表示层"
        WEB_UI[Web界面]
        REST_API[REST API]
        MCP_TOOLS[MCP工具]
    end
    
    subgraph "💼 应用层"
        APP_SVC[应用服务]
        DTO[数据传输对象]
        VALIDATORS[验证器]
    end
    
    subgraph "🏢 领域层"
        DOMAIN_SVC[领域服务]
        DOMAIN_MODEL[领域模型]
        INTERFACES[服务接口]
    end
    
    subgraph "🗃️ 基础设施层"
        REPOS[仓储实现]
        EXT_SVC[外部服务]
        UTILS[工具类]
    end
    
    %% 依赖关系
    WEB_UI --> REST_API
    REST_API --> APP_SVC
    MCP_TOOLS --> APP_SVC
    
    APP_SVC --> DOMAIN_SVC
    APP_SVC --> DTO
    APP_SVC --> VALIDATORS
    
    DOMAIN_SVC --> DOMAIN_MODEL
    DOMAIN_SVC --> INTERFACES
    
    REPOS -.implements.-> INTERFACES
    EXT_SVC -.implements.-> INTERFACES
    
    %% 样式
    classDef presentation fill:#e3f2fd
    classDef application fill:#f3e5f5
    classDef domain fill:#e8f5e8
    classDef infrastructure fill:#fff3e0
    
    class WEB_UI,REST_API,MCP_TOOLS presentation
    class APP_SVC,DTO,VALIDATORS application
    class DOMAIN_SVC,DOMAIN_MODEL,INTERFACES domain
    class REPOS,EXT_SVC,UTILS infrastructure
```

## 🔒 安全架构图

```mermaid
graph TB
    subgraph "🌐 外部访问"
        INTERNET[Internet]
        LOCAL[局域网]
    end
    
    subgraph "🛡️ 安全边界"
        subgraph "Web安全"
            CORS[CORS策略]
            HTTPS[HTTPS/TLS]
            AUTH[身份认证]
            AUTHZ[授权验证]
        end
        
        subgraph "数据安全"
            ENCRYPT[数据加密]
            BACKUP[备份策略]
            AUDIT[审计日志]
        end
        
        subgraph "API安全"
            RATE_LIMIT[速率限制]
            API_KEY[API密钥]
            VALIDATE[输入验证]
        end
    end
    
    subgraph "🏢 应用层"
        WEB_APP[Web应用]
        MCP_SERVER[MCP服务器]
        BG_TASKS[后台任务]
    end
    
    subgraph "💾 数据层"
        SQLITE[(SQLite)]
        QDRANT[(Qdrant)]
        FILES[文件系统]
    end
    
    %% 安全连接
    INTERNET --> HTTPS
    LOCAL --> AUTH
    
    HTTPS --> WEB_APP
    AUTH --> WEB_APP
    AUTHZ --> WEB_APP
    
    API_KEY --> MCP_SERVER
    RATE_LIMIT --> MCP_SERVER
    VALIDATE --> MCP_SERVER
    
    ENCRYPT --> SQLITE
    BACKUP --> SQLITE
    AUDIT --> SQLITE
    
    WEB_APP --> SQLITE
    MCP_SERVER --> QDRANT
    BG_TASKS --> FILES