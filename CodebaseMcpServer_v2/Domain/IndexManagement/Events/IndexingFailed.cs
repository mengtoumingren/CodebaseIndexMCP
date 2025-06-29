using CodebaseMcpServer.Domain.Shared;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;

namespace CodebaseMcpServer.Domain.IndexManagement.Events
{
    public record IndexingFailed(
        IndexLibraryId LibraryId,
        string ErrorMessage
    ) : DomainEventBase;
}