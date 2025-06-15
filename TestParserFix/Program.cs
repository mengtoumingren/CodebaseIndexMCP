using System;
using System.IO;
using CodebaseMcpServer.Services.Parsing;

namespace TestParserFix
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("测试 Roslyn 解析器栈溢出修复");
            Console.WriteLine("==============================");
            
            // 测试解析 CodeParserFactory.cs 文件
            var testFilePath = @"d:\VSProject\CoodeBaseApp\CodebaseMcpServer\Services\Parsing\CodeParserFactory.cs";
            
            if (!File.Exists(testFilePath))
            {
                Console.WriteLine($"❌ 测试文件不存在: {testFilePath}");
                return;
            }
            
            Console.WriteLine($"📁 测试文件: {testFilePath}");
            
            try
            {
                var parser = new CSharpRoslynParser();
                Console.WriteLine("🔍 开始解析...");
                
                var snippets = parser.ParseCodeFile(testFilePath);
                
                Console.WriteLine($"✅ 解析成功！");
                Console.WriteLine($"📦 提取到 {snippets.Count} 个代码片段");
                
                // 显示前几个片段的信息
                for (int i = 0; i < Math.Min(5, snippets.Count); i++)
                {
                    var snippet = snippets[i];
                    Console.WriteLine($"  {i + 1}. {snippet.MethodName} (行 {snippet.StartLine}-{snippet.EndLine})");
                }
                
                if (snippets.Count > 5)
                {
                    Console.WriteLine($"  ... 还有 {snippets.Count - 5} 个片段");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 解析失败: {ex.Message}");
                Console.WriteLine($"异常类型: {ex.GetType().Name}");
                
                if (ex.StackTrace != null)
                {
                    Console.WriteLine($"堆栈跟踪: {ex.StackTrace[..Math.Min(500, ex.StackTrace.Length)]}...");
                }
            }
            
            Console.WriteLine("\n测试完成。");
        }
    }
}