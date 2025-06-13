// 使用示例
using CodeSearch;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("[DEBUG] 程序开始执行");
            
            // 初始化搜索系统
            Console.WriteLine("[DEBUG] 初始化搜索系统...");
            var searchSystem = new CodeSemanticSearch(
                apiKey: "sk-a239bd73d5b947ed955d03d437ca1e70",
                collectionName: "csharp_code");
            Console.WriteLine("[DEBUG] 搜索系统初始化完成");

            // 处理代码库
            var codebasePath = @"D:\VSProject\WorkFlowEngine\workflow-engine\WorkFlowCore\WorkFlowCore.BusinessDemo.Web"; // 替换为实际C#代码库路径
            Console.WriteLine($"[DEBUG] 开始处理代码库: {codebasePath}");
            var indexedCount = await searchSystem.ProcessCodebase(codebasePath);
            Console.WriteLine($"已索引 {indexedCount} 个C#代码片段");

            // 执行搜索
            var query = "发起审批";
            Console.WriteLine($"[DEBUG] 开始搜索: {query}");
            var results = await searchSystem.Search(query, limit: 3);

            // 显示结果
            Console.WriteLine($"\n搜索结果 - 查询: '{query}'");
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                var snippet = result.Snippet;

                Console.WriteLine($"\n--- 结果 {i + 1} (得分: {result.Score:F4}) ---");
                Console.WriteLine($"文件: {snippet.FilePath}");
                if (!string.IsNullOrEmpty(snippet.Namespace))
                    Console.WriteLine($"命名空间: {snippet.Namespace}");
                if (!string.IsNullOrEmpty(snippet.ClassName))
                    Console.WriteLine($"类: {snippet.ClassName}");
                if (!string.IsNullOrEmpty(snippet.MethodName))
                    Console.WriteLine($"方法: {snippet.MethodName}");

                Console.WriteLine($"位置: 第 {snippet.StartLine}-{snippet.EndLine} 行");
                Console.WriteLine($"代码:\n{(snippet.Code.Length > 200 ? snippet.Code.Substring(0, 200) + "..." : snippet.Code)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[ERROR] 程序执行出错:");
            Console.WriteLine($"异常类型: {ex.GetType().Name}");
            Console.WriteLine($"错误消息: {ex.Message}");
            Console.WriteLine($"堆栈跟踪:\n{ex.StackTrace}");
            
            if (ex.InnerException != null)
            {
                Console.WriteLine($"\n内部异常: {ex.InnerException.GetType().Name}");
                Console.WriteLine($"内部异常消息: {ex.InnerException.Message}");
            }
        }

        Console.WriteLine("\n按任意键退出...");
        Console.ReadLine();
    }
}