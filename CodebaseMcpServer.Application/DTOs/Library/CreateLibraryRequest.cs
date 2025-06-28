using System.Collections.Generic;

namespace CodebaseMcpServer.Application.DTOs.Library
{
    /// <summary>
    /// 创建索引库的请求模型
    /// </summary>
    public class CreateLibraryRequest
    {
        public string CodebasePath { get; set; }
        public string? Name { get; set; }
        public List<string>? PresetIds { get; set; }
    }
}
