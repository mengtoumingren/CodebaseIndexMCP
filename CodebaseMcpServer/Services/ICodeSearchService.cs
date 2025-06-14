using CodebaseMcpServer.Models;

namespace CodebaseMcpServer.Services;

/// <summary>
/// 代码搜索服务接口
/// </summary>
public interface ICodeSearchService
{
    /// <summary>
    /// 执行语义搜索
    /// </summary>
    /// <param name="query">搜索查询</param>
    /// <param name="codebasePath">代码库路径，如果为null则使用默认路径</param>
    /// <param name="limit">返回结果数量限制</param>
    /// <returns>搜索结果列表</returns>
    Task<List<SearchResult>> SearchAsync(string query, string? codebasePath = null, int limit = 10);

    /// <summary>
    /// 检查代码库是否已被索引
    /// </summary>
    /// <param name="codebasePath">代码库路径</param>
    /// <returns>是否已索引</returns>
    Task<bool> IsCodebaseIndexedAsync(string codebasePath);
}