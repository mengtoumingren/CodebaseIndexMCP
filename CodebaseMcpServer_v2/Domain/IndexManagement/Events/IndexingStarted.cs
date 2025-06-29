using System;
using CodebaseMcpServer.Domain.Shared;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;

namespace CodebaseMcpServer.Domain.IndexManagement.Events
{
    public record IndexingStarted(
        IndexLibraryId LibraryId,
        CodebasePath CodebasePath,
        DateTime StartedAt
    ) : DomainEventBase;
}