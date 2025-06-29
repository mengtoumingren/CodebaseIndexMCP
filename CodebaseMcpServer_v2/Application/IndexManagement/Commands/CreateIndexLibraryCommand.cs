using CodebaseMcpServer.Application.Shared;

namespace CodebaseMcpServer.Application.IndexManagement.Commands
{
    public record CreateIndexLibraryCommand(
        string CodebasePath,
        string CollectionName
    ) : ICommand<Result<int>>;
}