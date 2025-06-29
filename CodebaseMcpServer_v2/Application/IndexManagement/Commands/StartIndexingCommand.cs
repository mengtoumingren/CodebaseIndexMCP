using CodebaseMcpServer.Application.Shared;

namespace CodebaseMcpServer.Application.IndexManagement.Commands
{
    public record StartIndexingCommand(
        int LibraryId
    ) : ICommand<Result>;
}