using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;
using CodebaseMcpServer.Domain.IndexManagement.Enums; // Added this line

namespace CodebaseMcpServer.Domain.IndexManagement.Services
{
    public interface IProjectTypeDetectionService
    {
        Task<ProjectType> DetectProjectTypeAsync(CodebasePath path, CancellationToken cancellationToken = default);
        WatchConfiguration GetRecommendedConfiguration(ProjectType projectType);
    }
}