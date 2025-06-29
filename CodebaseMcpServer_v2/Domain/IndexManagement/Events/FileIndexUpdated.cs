using CodebaseMcpServer.Domain.Shared;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;

namespace CodebaseMcpServer.Domain.IndexManagement.Events
{
    public record FileIndexUpdated(
        IndexLibraryId LibraryId,
        FilePath FilePath
    ) : DomainEventBase;
}