using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CodebaseMcpServer.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CodebaseMcpServer.Services
{
    /// <summary>
    /// 文件变更持久化服务 - 管理文件变更事件的本地存储和恢复
    /// </summary>
    public class FileChangePersistenceService
    {
        private readonly string _storePath;
        private readonly ILogger<FileChangePersistenceService> _logger;
        private readonly IConfiguration _configuration;
        private readonly object _fileLock = new object();

        public FileChangePersistenceService(
            ILogger<FileChangePersistenceService> logger,
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            var baseDir = _configuration.GetValue<string>("FileChangePersistence:StorageDirectory")
                ?? "file-changes-storage";
            _storePath = Path.Combine(Directory.GetCurrentDirectory(), baseDir);

            Directory.CreateDirectory(_storePath);
            _logger.LogInformation("文件变更持久化存储目录: {Path}", _storePath);
        }

        public async Task<bool> SaveChangeAsync(FileChangeEvent change)
        {
            try
            {
                var changeFile = Path.Combine(_storePath, $"{change.Id}.json");
                var persistedChange = new PersistedFileChange
                {
                    Change = change,
                    SavedAt = DateTime.UtcNow,
                    Version = "1.0"
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                };
                var json = JsonSerializer.Serialize(persistedChange, options);

                await File.WriteAllTextAsync(changeFile, json); // 使用异步方法

                _logger.LogDebug("文件变更已持久化: {Id} - {Path}", change.Id, (object)change.FilePath); // 显式转换避免二义性警告
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "保存文件变更失败: {Id}", change.Id);
                return false;
            }
        }

        public async Task<bool> UpdateChangeAsync(FileChangeEvent change)
        {
            // For simplicity, SaveChangeAsync will overwrite if the file exists.
            return await SaveChangeAsync(change);
        }

        public async Task<List<FileChangeEvent>> LoadPendingChangesAsync()
        {
            return await LoadChangesByStatusAsync(FileChangeStatus.Pending);
        }

        public async Task<List<FileChangeEvent>> LoadProcessingChangesAsync()
        {
            return await LoadChangesByStatusAsync(FileChangeStatus.Processing);
        }
        
        private async Task<List<FileChangeEvent>> LoadChangesByStatusAsync(FileChangeStatus status)
        {
            var changes = new List<FileChangeEvent>();
            try
            {
                var files = Directory.GetFiles(_storePath, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file); // 使用异步方法
                        var options = new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                        };
                        var persistedChange = JsonSerializer.Deserialize<PersistedFileChange>(json, options);
                        if (persistedChange?.Change != null && persistedChange.Change.Status == status)
                        {
                            changes.Add(persistedChange.Change);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "加载或反序列化文件变更失败: {File}", file);
                    }
                }
                _logger.LogInformation("加载了 {Count} 个状态为 {Status} 的文件变更", changes.Count, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载状态为 {Status} 的文件变更列表失败", status);
            }
            return changes;
        }


        public async Task<bool> CleanupChangeAsync(string changeId)
        {
            try
            {
                var changeFile = Path.Combine(_storePath, $"{changeId}.json");
                if (File.Exists(changeFile))
                {
                    File.Delete(changeFile); // File.Delete is synchronous, no async alternative
                    _logger.LogDebug("文件变更记录已清理: {Id}", changeId);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理文件变更记录失败: {Id}", changeId);
                return false;
            }
        }

        public async Task<int> CleanupExpiredChangesAsync(TimeSpan maxAge)
        {
            int cleanedCount = 0;
            try
            {
                var files = Directory.GetFiles(_storePath, "*.json");
                var expirationDate = DateTime.UtcNow - maxAge;

                foreach (var file in files)
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(file); // 使用异步方法
                         var options = new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                        };
                        var persistedChange = JsonSerializer.Deserialize<PersistedFileChange>(json, options);

                        if (persistedChange?.Change != null &&
                            (persistedChange.Change.Status == FileChangeStatus.Failed || persistedChange.Change.Status == FileChangeStatus.Expired) &&
                            persistedChange.SavedAt < expirationDate)
                        {
                           File.Delete(file); // File.Delete is synchronous, no async alternative
                            cleanedCount++;
                            _logger.LogDebug("清理了过期的文件变更记录: {Id}, 状态: {Status}, 保存于: {SavedAt}",
                                persistedChange.Change.Id, persistedChange.Change.Status, persistedChange.SavedAt);
                        }
                        else if (persistedChange?.Change != null && persistedChange.Change.Status == FileChangeStatus.Completed && persistedChange.SavedAt < expirationDate)
                        {
                            // Also clean up completed tasks that might have been missed if CleanupChangeAsync failed
                            File.Delete(file); // File.Delete is synchronous, no async alternative
                            cleanedCount++;
                            _logger.LogDebug("清理了过期的已完成文件变更记录: {Id}, 保存于: {SavedAt}",
                                persistedChange.Change.Id, persistedChange.SavedAt);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "清理过期文件变更记录时出错: {File}", file);
                    }
                }
                if (cleanedCount > 0)
                {
                    _logger.LogInformation("定期清理了 {Count} 个过期的文件变更记录", cleanedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "定期清理过期文件变更记录失败");
            }
            return cleanedCount;
        }
    }

    public class PersistedFileChange
    {
        public FileChangeEvent Change { get; set; } = new();
        public DateTime SavedAt { get; set; }
        public string Version { get; set; } = "1.0";
    }
}