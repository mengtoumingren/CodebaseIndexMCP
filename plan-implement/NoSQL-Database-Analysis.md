# CodebaseApp 非关系数据库选择分析

## 🎯 嵌入式NoSQL数据库对比

### 1. **LiteDB** (.NET专用文档数据库)

#### 优势：
```csharp
// LiteDB 使用示例
using LiteDB;

public class CodebaseLibrary
{
    public ObjectId Id { get; set; }
    public string Name { get; set; }
    public string Path { get; set; }
    public WatchConfiguration WatchConfig { get; set; }
    public List<FileDetail> Files { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

// 简单易用的API
using var db = new LiteDatabase("codebase.db");
var libraries = db.GetCollection<CodebaseLibrary>("libraries");

// LINQ支持
var activeLibraries = libraries.Find(x => x.IsActive).ToList();

// 索引支持
libraries.EnsureIndex(x => x.Path);
libraries.EnsureIndex("$.WatchConfig.IsEnabled");
```

#### 特点：
- **零配置**：单文件数据库，类似SQLite
- **LINQ支持**：原生支持.NET LINQ查询
- **JSON文档**：完全的文档型数据库
- **事务支持**：ACID事务保证
- **性能**：对小到中型数据集性能优秀
- **文件大小**：最大4GB限制

#### 代码示例：
```csharp
// Repository实现
public class LiteDbIndexLibraryRepository : IIndexLibraryRepository
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<IndexLibrary> _collection;

    public LiteDbIndexLibraryRepository(LiteDatabase db)
    {
        _db = db;
        _collection = _db.GetCollection<IndexLibrary>("libraries");
        
        // 创建索引
        _collection.EnsureIndex(x => x.CodebasePath);
        _collection.EnsureIndex(x => x.Status);
        _collection.EnsureIndex(x => x.CreatedAt);
    }

    public async Task<IndexLibrary> CreateAsync(IndexLibrary library)
    {
        library.Id = ObjectId.NewObjectId();
        library.CreatedAt = DateTime.UtcNow;
        
        _collection.Insert(library);
        return library;
    }

    public async Task<List<IndexLibrary>> GetActiveLibrariesAsync()
    {
        return _collection.Find(x => x.IsActive)
                          .OrderBy(x => x.UpdatedAt)
                          .ToList();
    }

    public async Task<IndexLibrary?> GetByPathAsync(string path)
    {
        return _collection.FindOne(x => x.CodebasePath == path);
    }
}
```

---

### 2. **MongoDB.Embedded** (MongoDB嵌入式版本)

#### 优势：
```csharp
// MongoDB嵌入式使用
using MongoDB.Driver;
using MongoDB.Embedded;

public class MongoDbService
{
    private readonly IMongoDatabase _database;
    
    public MongoDbService()
    {
        // 启动嵌入式MongoDB
        var runner = EmbeddedMongoDbRunner.Start();
        var client = new MongoClient(runner.ConnectionString);
        _database = client.GetDatabase("codebase");
    }
}

// 灵活的文档结构
public class CodebaseDocument
{
    [BsonId]
    public ObjectId Id { get; set; }
    
    [BsonElement("name")]
    public string Name { get; set; }
    
    [BsonElement("config")]
    public BsonDocument Configuration { get; set; }
    
    [BsonElement("files")]
    public List<BsonDocument> Files { get; set; }
}
```

#### 特点：
- **功能完整**：完整的MongoDB功能
- **查询强大**：复杂的聚合查询支持
- **扩展性好**：可以无缝升级到MongoDB集群
- **学习成本**：需要熟悉MongoDB查询语法
- **资源消耗**：相对较高的内存和磁盘占用

---

### 3. **RavenDB.Embedded** (文档数据库)

#### 特点：
- **ACID事务**：完整的事务支持
- **高性能**：针对.NET优化
- **复杂查询**：支持复杂的文档查询
- **成本**：商业产品，有许可成本

---

### 4. **Realm Database** (移动优先)

#### 特点：
- **对象数据库**：直接操作.NET对象
- **实时同步**：内置实时数据同步
- **跨平台**：支持多平台
- **学习曲线**：需要适应对象数据库概念

---

## 📊 详细对比分析

| 特性 | SQLite | LiteDB | MongoDB.Embedded | 备注 |
|------|--------|---------|------------------|------|
| **学习成本** | 低 | 低 | 中 | SQLite/LiteDB都很简单 |
| **配置复杂度** | 零配置 | 零配置 | 需要配置 | LiteDB最简单 |
| **查询能力** | SQL(强) | LINQ(强) | MongoDB语法(很强) | 都能满足需求 |
| **事务支持** | 完整 | 完整 | 完整 | 三者都支持ACID |
| **性能** | 优秀 | 良好 | 优秀 | 对于中小型数据都够用 |
| **文件大小限制** | 281TB | 4GB | 无限制 | LiteDB有限制但够用 |
| **生态系统** | 成熟 | 中等 | 成熟 | SQLite生态最完善 |
| **调试工具** | 丰富 | 基础 | 丰富 | SQLite工具最多 |

## 🎯 针对CodebaseApp的建议

### 场景分析：

**当前需求特点：**
- 数据量：中小型（几十到几百个索引库）
- 查询：相对简单的CRUD + 统计查询
- 关系：有一定的关系性但不复杂
- 部署：单机部署，追求简单

### 推荐方案对比：

#### 🥇 **推荐选择1：LiteDB**

**适用场景：**
```csharp
// 数据模型
public class IndexLibrary
{
    [BsonId]
    public ObjectId Id { get; set; }
    
    public string Name { get; set; }
    public string CodebasePath { get; set; }
    public string CollectionName { get; set; }
    public IndexStatus Status { get; set; }
    
    // 嵌套的监控配置
    public WatchConfiguration WatchConfig { get; set; }
    
    // 文件详情数组
    public List<FileIndexDetail> Files { get; set; } = new();
    
    // 任务历史
    public List<TaskRecord> TaskHistory { get; set; } = new();
    
    // 可扩展的元数据
    public BsonDocument Metadata { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// 使用示例
var activeLibraries = await libraryRepo.QueryAsync(lib => 
    lib.Status == IndexStatus.Active && 
    lib.WatchConfig.IsEnabled);

var statistics = await libraryRepo.AggregateAsync(libs => new {
    TotalFiles = libs.SelectMany(l => l.Files).Count(),
    TotalSnippets = libs.Sum(l => l.IndexedSnippets)
});
```

**优势：**
- ✅ 零配置，单文件部署
- ✅ 原生.NET支持，LINQ查询
- ✅ 支持嵌套文档，减少JOIN
- ✅ 事务支持，数据一致性保证
- ✅ 相比JSON文件有更好的查询性能
- ✅ 支持索引，查询优化

**劣势：**
- ❌ 4GB文件大小限制（但对CodebaseApp足够）
- ❌ 相对较新，生态不如SQLite成熟
- ❌ 复杂查询能力不如SQL

#### 🥈 **推荐选择2：保持SQLite（关系型）**

**优势：**
- ✅ 成熟稳定，生态完善
- ✅ SQL查询能力强大
- ✅ 调试工具丰富
- ✅ 团队熟悉度高
- ✅ 支持复杂的关联查询和统计

**劣势：**
- ❌ Schema相对固定
- ❌ JSON数据需要序列化
- ❌ 关联查询复杂度较高

### 💡 混合方案建议

**最佳实践：SQLite + JSON列**

```sql
-- 利用SQLite的JSON支持
CREATE TABLE IndexLibraries (
    Id INTEGER PRIMARY KEY,
    Name TEXT NOT NULL,
    CodebasePath TEXT UNIQUE NOT NULL,
    Status TEXT NOT NULL,
    
    -- JSON列存储灵活数据
    WatchConfig JSON,
    FileDetails JSON,
    Metadata JSON,
    
    CreatedAt DATETIME,
    UpdatedAt DATETIME
);

-- JSON查询示例
SELECT * FROM IndexLibraries 
WHERE JSON_EXTRACT(WatchConfig, '$.isEnabled') = true;

SELECT 
    Name,
    JSON_ARRAY_LENGTH(FileDetails) as FileCount
FROM IndexLibraries;
```

这样可以兼得关系型数据库的稳定性和文档数据库的灵活性。

## 🎯 最终建议

### 推荐顺序：

1. **SQLite + JSON列** (推荐80%)
   - 保持现有技术栈稳定性
   - 利用SQLite 3.45+的强大JSON支持
   - 兼顾关系性和灵活性

2. **LiteDB** (推荐15%)
   - 如果团队愿意尝试新技术
   - 特别适合文档型数据模型
   - 更好的.NET集成

3. **纯SQLite关系型** (推荐5%)
   - 如果未来需要复杂的关联查询
   - 团队更偏好传统SQL

### 代码迁移工作量对比：

| 方案 | 迁移工作量 | 学习成本 | 长期维护 |
|------|-----------|----------|----------|
| SQLite + JSON | 中等 | 低 | 容易 |
| LiteDB | 中等 | 中等 | 中等 |
| 纯SQLite | 低 | 低 | 容易 |

基于CodebaseApp的实际需求，我建议采用 **SQLite + JSON列** 的混合方案，这样可以在保持技术栈稳定的同时，获得文档数据库的灵活性优势。