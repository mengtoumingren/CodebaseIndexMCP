using System.Text.Json;

namespace CodebaseMcpServer.Services.Data;

/// <summary>
/// JSON查询辅助类 - SQLite JSON函数封装
/// </summary>
public static class JsonQueryHelper
{
    /// <summary>
    /// 提取JSON路径值
    /// </summary>
    public static string ExtractPath(string jsonColumn, string path)
    {
        return $"JSON_EXTRACT({jsonColumn}, '$.{path}')";
    }
    
    /// <summary>
    /// 获取JSON数组长度
    /// </summary>
    public static string ArrayLength(string jsonColumn, string arrayPath = "")
    {
        var path = string.IsNullOrEmpty(arrayPath) ? "" : $".{arrayPath}";
        return $"JSON_ARRAY_LENGTH({jsonColumn}, '${path}')";
    }
    
    /// <summary>
    /// 设置JSON路径值
    /// </summary>
    public static string JsonSet(string jsonColumn, string path, object value)
    {
        return $"JSON_SET({jsonColumn}, '$.{path}', {FormatValue(value)})";
    }
    
    /// <summary>
    /// 插入JSON路径值
    /// </summary>
    public static string JsonInsert(string jsonColumn, string path, object value)
    {
        return $"JSON_INSERT({jsonColumn}, '$.{path}', {FormatValue(value)})";
    }
    
    /// <summary>
    /// 移除JSON路径
    /// </summary>
    public static string JsonRemove(string jsonColumn, string path)
    {
        return $"JSON_REMOVE({jsonColumn}, '$.{path}')";
    }
    
    /// <summary>
    /// 验证JSON格式
    /// </summary>
    public static string ValidateJson(string jsonColumn)
    {
        return $"JSON_VALID({jsonColumn})";
    }
    
    /// <summary>
    /// JSON类型检查
    /// </summary>
    public static string JsonType(string jsonColumn, string path = "")
    {
        var pathStr = string.IsNullOrEmpty(path) ? "$" : $"$.{path}";
        return $"JSON_TYPE({jsonColumn}, '{pathStr}')";
    }
    
    /// <summary>
    /// JSON EACH 遍历
    /// </summary>
    public static string JsonEach(string jsonColumn, string path = "")
    {
        var pathStr = string.IsNullOrEmpty(path) ? "" : $", '$.{path}'";
        return $"JSON_EACH({jsonColumn}{pathStr})";
    }
    
    /// <summary>
    /// 格式化值为SQL参数
    /// </summary>
    private static string FormatValue(object value)
    {
        return value switch
        {
            string s => $"'{EscapeString(s)}'",
            bool b => b.ToString().ToLower(),
            null => "null",
            _ when value.GetType().IsArray || (value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(List<>)) => 
                $"'{EscapeString(JsonSerializer.Serialize(value))}'",
            _ when value.GetType().IsClass && value.GetType() != typeof(string) =>
                $"'{EscapeString(JsonSerializer.Serialize(value))}'",
            _ => value.ToString()
        };
    }
    
    /// <summary>
    /// 转义SQL字符串
    /// </summary>
    private static string EscapeString(string input)
    {
        return input.Replace("'", "''");
    }
    
    /// <summary>
    /// 构建JSON搜索条件
    /// </summary>
    public static string JsonContains(string jsonColumn, string path, string searchValue)
    {
        return $"{ExtractPath(jsonColumn, path)} LIKE '%{EscapeString(searchValue)}%'";
    }
    
    /// <summary>
    /// 构建JSON数组包含条件
    /// </summary>
    public static string JsonArrayContains(string jsonColumn, string arrayPath, string searchValue)
    {
        return $"EXISTS (SELECT 1 FROM JSON_EACH({jsonColumn}, '$.{arrayPath}') WHERE value = '{EscapeString(searchValue)}')";
    }
    
    /// <summary>
    /// 构建复合JSON查询条件
    /// </summary>
    public static class Conditions
    {
        public static string IsEnabled(string jsonColumn)
        {
            // SQLite's JSON_EXTRACT returns a JSON boolean, which is best compared against integer 1 for TRUE.
            return $"{ExtractPath(jsonColumn, "isEnabled")} = 1";
        }
        
        public static string ProjectType(string jsonColumn, string projectType)
        {
            return $"{ExtractPath(jsonColumn, "projectType")} = '{EscapeString(projectType)}'";
        }
        
        public static string Team(string jsonColumn, string team)
        {
            return $"{ExtractPath(jsonColumn, "team")} = '{EscapeString(team)}'";
        }
        
        public static string HasTag(string jsonColumn, string tag)
        {
            return JsonArrayContains(jsonColumn, "tags", tag);
        }
        
        public static string Priority(string jsonColumn, string priority)
        {
            return $"{ExtractPath(jsonColumn, "priority")} = '{EscapeString(priority)}'";
        }
    }
}