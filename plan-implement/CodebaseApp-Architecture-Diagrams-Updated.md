# CodebaseApp 升级架构图表设计 (SQLite + JSON方案)

## 🏗️ 整体系统架构图

```mermaid
graph TB
    subgraph "🌐 用户接口层"
        WEB[Web管理界面<br/>HTML5 + Bootstrap + Chart.js]
        MCP[MCP客户端<br/>Claude/Cursor等]
        API_DOC[API文档<br/>Swagger/OpenAPI]
    end
    
    subgraph "🔌 接口层"
        REST[REST API<br/>ASP.NET Core Controllers]
        MCP_SERVER[MCP服务器<br/>兼容现有工具]
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
        DETECT[项目检测器<br/>智能类型识别]
        MONITOR[系统监控<br/>健康检查/指标]
    end
    
    subgraph "💾 数据层 (SQLite + JSON混合)"
        SQLITE[(SQLite数据库<br/>关系型数据 + JSON列)]
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
    IDX_SVC --> DETECT
    TASK_SVC --> MONITOR
    
    %% 数据层连接
    IDX_SVC --> SQLITE
    IDX_SVC --> QDRANT
    WATCH_SVC --> SQLITE
    TASK_SVC --> SQLITE
    CONFIG_SVC --> SQLITE
    PARSER --> FILES
    
    %% 样式
    classDef interface fill:#e1f5fe
    classDef business fill:#f3e5f5
    classDef infrastructure fill:#fff3e0
    classDef data fill:#e8f5e8
    
    class WEB,MCP,API_DOC,REST,MCP_SERVER,SIGNALR interface
    class IDX_SVC,WATCH_SVC,TASK_SVC,CONFIG_SVC business
    class EMBED,PARSER,DETECT,MONITOR infrastructure
    class SQLITE,QDRANT,FILES data
```

## 📊 SQLite + JSON 混合数据库架构图

```mermaid
erDiagram
    IndexLibraries ||--o{ FileIndexDetails : "contains"
    IndexLibraries ||--o{ BackgroundTasks : "processes"
    IndexLibraries ||--o{ FileChangeEvents : "monitors"
    
    IndexLibraries {
        int Id PK
        varchar Name
        varchar CodebasePath UK
        varchar CollectionName UK
        varchar Status
        
        json WatchConfig "配置JSON"
        json Statistics "统计JSON"
        json Metadata "元数据JSON"
        
        datetime CreatedAt
        datetime UpdatedAt
        datetime LastIndexedAt
        int TotalFiles
        int IndexedSnippets
        boolean IsActive
    }
    
    FileIndexDetails {
        int Id PK
        int LibraryId FK
        varchar RelativeFilePath
        datetime LastIndexedAt
        int FileSize
        varchar FileHash
        int SnippetCount
        
        json FileMetadata "文件元数据JSON"
        
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
        
        json TaskConfig "任务配置JSON"
        json TaskResult "任务结果JSON"
        
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
        
        json EventDetails "事件详情JSON"
        
        datetime ProcessedAt
        text ErrorMessage
        int RetryCount
        datetime CreatedAt
        datetime UpdatedAt
    }
    
    SystemConfigurations {
        int Id PK
        varchar ConfigKey UK
        json ConfigValue "配置值JSON"
        varchar ConfigType
        text Description
        boolean IsEditable
        datetime CreatedAt
        datetime UpdatedAt
    }
```

## 🔄 JSON数据结构设计图

```mermaid
graph TB
    subgraph "WatchConfig JSON 结构"
        WC[WatchConfig]
        WC --> WC_FP[filePatterns: string[]]
        WC --> WC_EP[excludePatterns: string[]]
        WC --> WC_IS[includeSubdirectories: boolean]
        WC --> WC_EN[isEnabled: boolean]
        WC --> WC_MFS[maxFileSize: number]
        WC --> WC_CF[customFilters: object[]]
    end
    
    subgraph "Statistics JSON 结构"
        ST[Statistics]
        ST --> ST_IS[indexedSnippets: number]
        ST --> ST_TF[totalFiles: number]
        ST --> ST_LID[lastIndexingDuration: number]
        ST --> ST_AFS[averageFileSize: number]
        ST --> ST_LD[languageDistribution: object]
        ST --> ST_IH[indexingHistory: object[]]
    end
    
    subgraph "Metadata JSON 结构"
        MD[Metadata]
        MD --> MD_PT[projectType: string]
        MD --> MD_FW[framework: string]
        MD --> MD_TM[team: string]
        MD --> MD_PR[priority: string]
        MD --> MD_TG[tags: string[]]
        MD --> MD_CS[customSettings: object]
    end
    
    subgraph "EventDetails JSON 结构"
        ED[EventDetails]
        ED --> ED_FS[fileSize: number]
        ED --> ED_DA[detectedAt: datetime]
        ED --> ED_TP[triggerPattern: string]
        ED --> ED_CM[changeMetadata: object]
    end
```

## 🔄 业务流程图 (SQLite + JSON)

### 索引库创建流程

```mermaid
sequenceDiagram
    participant U as 用户
    participant W as Web界面
    participant API as REST API
    participant IDX as 索引库服务
    participant DET as 项目检测器
    participant DB as SQLite+JSON
    participant TSK as 后台任务服务
    participant QDR as Qdrant
    
    U->>W: 创建新索引库
    W->>API: POST /api/libraries
    API->>IDX: CreateAsync(request)
    
    IDX->>DET: 检测项目类型
    DET-->>IDX: 返回推荐JSON配置
    
    Note over IDX: 构建JSON配置对象
    IDX->>IDX: 创建WatchConfig JSON
    IDX->>IDX: 创建Metadata JSON
    IDX->>IDX: 初始化Statistics JSON
    
    IDX->>DB: INSERT INTO IndexLibraries<br/>(关系型字段 + JSON列)
    
    IDX->>TSK: 排队索引任务
    TSK->>DB: INSERT INTO BackgroundTasks<br/>(TaskConfig JSON)
    
    TSK->>QDR: 创建向量集合
    TSK->>TSK: 开始索引处理
    
    loop 文件批处理
        TSK->>TSK: 解析代码文件
        TSK->>QDR: 批量索引向量
        TSK->>DB: UPDATE BackgroundTasks<br/>(TaskResult JSON)
    end
    
    TSK->>DB: UPDATE Statistics JSON
    TSK->>W: 推送完成通知(SignalR)
    W-->>U: 显示完成状态
```

### JSON配置更新流程

```mermaid
sequenceDiagram
    participant U as 用户
    participant W as Web界面
    participant API as REST API
    participant IDX as 索引库服务
    participant DB as SQLite+JSON
    participant FS as 文件监视服务
    
    U->>W: 修改监控配置
    W->>API: PUT /api/libraries/{id}/watch-config
    API->>IDX: UpdateWatchConfigurationAsync()
    
    IDX->>DB: SELECT WatchConfig JSON
    Note over IDX: 解析现有JSON配置
    IDX->>IDX: 合并新配置到JSON对象
    
    IDX->>DB: UPDATE IndexLibraries<br/>SET WatchConfig = new_json
    
    Note over IDX: 通知文件监视服务
    IDX->>FS: 重启文件监控器
    FS->>DB: 读取新的WatchConfig JSON
    FS->>FS: 应用新的文件过滤规则
    
    FS-->>API: 配置更新成功
    API-->>W: 返回成功状态
    W-->>U: 显示更新结果
```

## 🌐 Web界面结构图 (JSON配置管理)

```mermaid
graph TB
    subgraph "📱 Web管理界面"
        DASH[🏠 仪表板<br/>Dashboard]
        LIB[📚 索引库管理<br/>Libraries]
        TASK[⚙️ 任务监控<br/>Tasks]
        CONF[🔧 系统配置<br/>Configuration]
        MON[📊 监控中心<br/>Monitoring]
    end
    
    subgraph "📚 索引库组件 (JSON配置)"
        LIB --> LIB_LIST[库列表<br/>显示JSON统计]
        LIB --> LIB_CREATE[创建向导<br/>项目类型检测]
        LIB --> LIB_DETAIL[详情视图<br/>JSON配置展示]
        LIB --> LIB_CONFIG[配置编辑器<br/>动态JSON表单]
    end
    
    subgraph "🔧 JSON配置编辑器"
        LIB_CONFIG --> WATCH_FORM[监控配置表单]
        LIB_CONFIG --> META_FORM[元数据编辑器]
        LIB_CONFIG --> PRESET_SEL[项目预设选择]
        LIB_CONFIG --> CUSTOM_JSON[自定义JSON编辑]
        
        WATCH_FORM --> FILE_PATTERNS[文件模式配置]
        WATCH_FORM --> EXCLUDE_PATTERNS[排除模式配置]
        WATCH_FORM --> SIZE_LIMITS[文件大小限制]
        WATCH_FORM --> CUSTOM_FILTERS[自定义过滤器]
    end
    
    subgraph "📊 JSON数据可视化"
        DASH --> STATS_CHART[统计图表<br/>从Statistics JSON]
        DASH --> LANG_DIST[语言分布<br/>从languageDistribution]
        DASH --> PROJ_TYPE[项目类型分布<br/>从Metadata JSON]
        DASH --> RECENT_ACT[最近活动<br/>从EventDetails JSON]
    end
```

## 🚀 部署架构图 (SQLite + JSON)

```mermaid
graph TB
    subgraph "🌐 客户端"
        BROWSER[Web浏览器<br/>JSON配置界面]
        MCP_CLIENT[MCP客户端<br/>Claude/Cursor]
    end
    
    subgraph "🖥️ 应用服务器"
        subgraph "ASP.NET Core应用"
            WEB_HOST[Web主机<br/>JSON API支持]
            MCP_HOST[MCP主机<br/>兼容现有工具]
            BG_HOST[后台服务主机<br/>JSON任务配置]
        end
        
        subgraph "静态资源"
            STATIC[wwwroot/静态文件<br/>JSON配置编辑器]
        end
    end
    
    subgraph "💾 数据存储 (混合模式)"
        SQLITE_DB[(SQLite数据库<br/>关系型 + JSON列)]
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
    
    %% JSON数据流
    SQLITE_DB -.JSON查询.-> WEB_HOST
    SQLITE_DB -.JSON更新.-> BG_HOST
```

## 📦 SQLite + JSON 数据访问模式

```mermaid
graph TB
    subgraph "Repository层 (混合模式)"
        REPO[Repository接口]
        REPO --> REL_OPS[关系型操作<br/>基础CRUD]
        REPO --> JSON_OPS[JSON操作<br/>配置管理]
    end
    
    subgraph "SQLite查询类型"
        REL_OPS --> REL_QUERY[关系查询<br/>SELECT * FROM Libraries<br/>WHERE Status = 'active']
        JSON_OPS --> JSON_QUERY[JSON查询<br/>WHERE JSON_EXTRACT(WatchConfig, '$.isEnabled') = true]
        JSON_OPS --> JSON_UPDATE[JSON更新<br/>UPDATE SET WatchConfig = JSON_SET(...)]
        JSON_OPS --> JSON_STATS[JSON统计<br/>JSON_ARRAY_LENGTH, SUM等]
    end
    
    subgraph "性能优化"
        JSON_QUERY --> JSON_INDEX[JSON索引<br/>CREATE INDEX ON JSON_EXTRACT(...)]
        REL_QUERY --> REL_INDEX[关系索引<br/>CREATE INDEX ON (column)]
        JSON_UPDATE --> JSON_VALIDATE[JSON验证<br/>确保数据完整性]
    end
    
    subgraph "缓存策略"
        JSON_OPS --> JSON_CACHE[JSON配置缓存<br/>IMemoryCache]
        REL_OPS --> REL_CACHE[关系数据缓存<br/>EF Core缓存]
    end
```

## 🔒 安全架构图 (JSON数据保护)

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
        
        subgraph "数据安全 (JSON保护)"
            JSON_VALID[JSON验证<br/>Schema校验]
            JSON_ESCAPE[JSON转义<br/>注入防护]
            ENCRYPT[敏感数据加密]
            BACKUP[JSON配置备份]
            AUDIT[JSON变更审计]
        end
        
        subgraph "API安全"
            RATE_LIMIT[速率限制]
            API_KEY[API密钥]
            INPUT_VALID[输入验证<br/>JSON格式检查]
        end
    end
    
    subgraph "🏢 应用层"
        WEB_APP[Web应用<br/>JSON配置管理]
        MCP_SERVER[MCP服务器<br/>JSON兼容]
        BG_TASKS[后台任务<br/>JSON任务配置]
    end
    
    subgraph "💾 数据层"
        SQLITE[(SQLite + JSON<br/>事务保护)]
        QDRANT[(Qdrant)]
        FILES[文件系统]
    end
    
    %% 安全连接
    INTERNET --> HTTPS
    LOCAL --> AUTH
    
    HTTPS --> WEB_APP
    AUTH --> WEB_APP
    AUTHZ --> WEB_APP
    
    JSON_VALID --> WEB_APP
    JSON_ESCAPE --> WEB_APP
    INPUT_VALID --> MCP_SERVER
    
    ENCRYPT --> SQLITE
    BACKUP --> SQLITE
    AUDIT --> SQLITE
    
    WEB_APP --> SQLITE
    MCP_SERVER --> QDRANT
    BG_TASKS --> FILES
```

这个更新的架构图完全反映了SQLite + JSON混合方案的设计，突出了JSON配置的灵活性和SQLite关系型数据的稳定性优势。