using CodebaseMcpServer.Domain.Entities;

namespace CodebaseMcpServer.Application.Services;

/// <summary>
/// 迁移结果
/// </summary>
public class MigrationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<IndexLibrary> MigratedLibraries { get; set; } = new();
}

/// <summary>
/// 迁移服务接口
/// </summary>
public interface IJsonMigrationService
{
    Task<MigrationResult> MigrateFromLegacyAsync();
}
