using CodebaseMcpServer.Domain.Entities;
using CodebaseMcpServer.Domain.Repositories;
using CodebaseMcpServer.Domain.ValueObjects;
using CodebaseMcpServer.Infrastructure.Persistence.Context;
using Dapper;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CodebaseMcpServer.Infrastructure.Persistence.Repositories;

/// <summary>
/// 索引库Repository实现 - SQLite + JSON支持
/// </summary>
public class IndexLibraryRepository : IIndexLibraryRepository
{
    private readonly DatabaseContext _context;
    private readonly ILogger<IndexLibraryRepository> _logger;

    public IndexLibraryRepository(DatabaseContext context, ILogger<IndexLibraryRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task AddAsync(IndexLibrary library)
    {
        var sql = @"
            INSERT INTO IndexLibraries 
            (Name, CodebasePath, CollectionName, Status, WatchConfig, Statistics, Metadata, 
             CreatedAt, UpdatedAt, LastIndexedAt, TotalFiles, IndexedSnippets, IsActive)
            VALUES 
            (@Name, @CodebasePath, @CollectionName, @Status, @WatchConfig, @Statistics, @Metadata,
             @CreatedAt, @UpdatedAt, @LastIndexedAt, @TotalFiles, @IndexedSnippets, @IsActive);
            
            SELECT last_insert_rowid();";

        var id = await _context.Connection.QuerySingleAsync<int>(sql, new
        {
            library.Name,
            library.CodebasePath,
            library.CollectionName,
            Status = library.Status.ToString(),
            library.WatchConfig,
            library.Statistics,
            library.Metadata,
            library.CreatedAt,
            library.UpdatedAt,
            library.LastIndexedAt,
            library.TotalFiles,
            library.IndexedSnippets,
            library.IsActive
        });
        
        library.Id = id;
        
        _logger.LogInformation("创建索引库: {Name} (ID: {Id})", library.Name, id);
    }

    public async Task<IndexLibrary?> GetByIdAsync(int id)
    {
        var sql = @"
            SELECT * FROM IndexLibraries 
            WHERE Id = @Id AND IsActive = 1";
        
        return await _context.Connection.QueryFirstOrDefaultAsync<IndexLibrary>(sql, new { Id = id });
    }

    public async Task<IEnumerable<IndexLibrary>> GetAllAsync()
    {
        var sql = @"
            SELECT * FROM IndexLibraries 
            WHERE IsActive = 1
            ORDER BY UpdatedAt DESC";
        
        var results = await _context.Connection.QueryAsync<IndexLibrary>(sql);
        return results;
    }

    public async Task UpdateAsync(IndexLibrary library)
    {
        var sql = @"
            UPDATE IndexLibraries 
            SET Name = @Name,
                CodebasePath = @CodebasePath,
                CollectionName = @CollectionName,
                Status = @Status,
                WatchConfig = @WatchConfig,
                Statistics = @Statistics,
                Metadata = @Metadata,
                UpdatedAt = @UpdatedAt,
                LastIndexedAt = @LastIndexedAt,
                TotalFiles = @TotalFiles,
                IndexedSnippets = @IndexedSnippets,
                IsActive = @IsActive
            WHERE Id = @Id";
            
        library.UpdatedAt = DateTime.UtcNow;
        
        await _context.Connection.ExecuteAsync(sql, new
        {
            library.Id,
            library.Name,
            library.CodebasePath,
            library.CollectionName,
            Status = library.Status.ToString(),
            library.WatchConfig,
            library.Statistics,
            library.Metadata,
            library.UpdatedAt,
            library.LastIndexedAt,
            library.TotalFiles,
            library.IndexedSnippets,
            library.IsActive
        });
        
        _logger.LogInformation("更新索引库: {Name} (ID: {Id})", library.Name, library.Id);
    }

    public async Task DeleteAsync(int id)
    {
        var sql = @"
            DELETE FROM IndexLibraries
            WHERE Id = @Id";
            
        await _context.Connection.ExecuteAsync(sql, new { Id = id });
        
        _logger.LogInformation("物理删除索引库: ID={Id}", id);
    }
}
