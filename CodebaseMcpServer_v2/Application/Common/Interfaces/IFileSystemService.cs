using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace CodebaseMcpServer.Application.Common.Interfaces
{
    public interface IFileSystemService
    {
        bool DirectoryExists(string path);
        bool FileExists(string path);
        Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
        Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default);
        IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly);
        IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly);
        FileInfo GetFileInfo(string path);
        DirectoryInfo GetDirectoryInfo(string path);
    }
}