using CodebaseMcpServer.Domain.Shared;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;

namespace CodebaseMcpServer.Domain.IndexManagement.Events
{
    public record WatchingEnabled(
        IndexLibraryId LibraryId,
        WatchConfiguration Config
    ) : DomainEventBase;
}