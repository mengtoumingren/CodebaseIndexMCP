using System.Security.Cryptography;
using System.Text;

namespace CodebaseMcpServer.Extensions;

/// <summary>
/// 路径处理扩展方法
/// </summary>
public static class PathExtensions
{
    /// <summary>
    /// 生成基于路径哈希的集合名称
    /// </summary>
    /// <param name="path">目录路径</param>
    /// <returns>集合名称，格式：code_index_{hash8位}</returns>
    public static string GenerateCollectionName(this string path)
    {
        var normalizedPath = Path.GetFullPath(path).ToLowerInvariant();
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(normalizedPath));
        var hashString = Convert.ToHexString(hash)[..8].ToLowerInvariant();
        return $"code_index_{hashString}";
    }
    
    /// <summary>
    /// 标准化路径格式
    /// </summary>
    /// <param name="path">原始路径</param>
    /// <returns>标准化后的路径</returns>
    public static string NormalizePath(this string path)
    {
        return Path.GetFullPath(path).ToLowerInvariant();
    }
    
    /// <summary>
    /// 生成唯一ID
    /// </summary>
    /// <returns>GUID字符串</returns>
    public static string GenerateUniqueId()
    {
        return Guid.NewGuid().ToString();
    }
    
    /// <summary>
    /// 检查路径是否应该被排除
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="excludeDirectories">排除的目录列表</param>
    /// <returns>如果应该排除返回true</returns>
    public static bool IsExcludedPath(this string filePath, List<string> excludeDirectories)
    {
        var pathParts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        
        return excludeDirectories.Any(excludeDir => 
            pathParts.Any(part => part.Equals(excludeDir, StringComparison.OrdinalIgnoreCase)));
    }
    
    /// <summary>
    /// 检查文件扩展名是否被支持
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="supportedExtensions">支持的扩展名列表</param>
    /// <returns>如果支持返回true</returns>
    public static bool IsSupportedExtension(this string filePath, List<string> supportedExtensions)
    {
        var extension = Path.GetExtension(filePath);
        return supportedExtensions.Any(ext => 
            ext.Equals(extension, StringComparison.OrdinalIgnoreCase));
    }
    
    /// <summary>
    /// 获取相对路径
    /// </summary>
    /// <param name="fullPath">完整路径</param>
    /// <param name="basePath">基础路径</param>
    /// <returns>相对路径</returns>
    public static string GetRelativePath(this string fullPath, string basePath)
    {
        var fullUri = new Uri(fullPath);
        var baseUri = new Uri(basePath.EndsWith(Path.DirectorySeparatorChar.ToString())
            ? basePath
            : basePath + Path.DirectorySeparatorChar);
            
        return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// 检查路径是否为另一个路径的子目录
    /// </summary>
    /// <param name="childPath">子路径</param>
    /// <param name="parentPath">父路径</param>
    /// <returns>如果是子目录返回true</returns>
    public static bool IsSubDirectoryOf(this string childPath, string parentPath)
    {
        var normalizedChild = childPath.NormalizePath();
        var normalizedParent = parentPath.NormalizePath();
        
        // 确保父路径以路径分隔符结尾
        if (!normalizedParent.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            normalizedParent += Path.DirectorySeparatorChar;
        }
        
        return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 获取两个路径之间的层级差距
    /// </summary>
    /// <param name="childPath">子路径</param>
    /// <param name="parentPath">父路径</param>
    /// <returns>层级数，如果不是子目录返回-1</returns>
    public static int GetDirectoryDepth(this string childPath, string parentPath)
    {
        if (!childPath.IsSubDirectoryOf(parentPath))
            return -1;
            
        var normalizedChild = childPath.NormalizePath();
        var normalizedParent = parentPath.NormalizePath();
        
        if (!normalizedParent.EndsWith(Path.DirectorySeparatorChar.ToString()))
        {
            normalizedParent += Path.DirectorySeparatorChar;
        }
        
        var relativePath = normalizedChild.Substring(normalizedParent.Length);
        return relativePath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}