using CodebaseMcpServer.Domain.Shared;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;

namespace CodebaseMcpServer.Domain.IndexManagement.Events
{
    public record WatchingDisabled(
        IndexLibraryId LibraryId
    ) : DomainEventBase;
}