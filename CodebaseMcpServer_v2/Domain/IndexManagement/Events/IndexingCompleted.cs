using CodebaseMcpServer.Domain.Shared;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;

namespace CodebaseMcpServer.Domain.IndexManagement.Events
{
    public record IndexingCompleted(
        IndexLibraryId LibraryId,
        IndexResult Result
    ) : DomainEventBase;
}