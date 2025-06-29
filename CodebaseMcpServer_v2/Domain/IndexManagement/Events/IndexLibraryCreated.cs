using System;
using CodebaseMcpServer.Domain.Shared;
using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;

namespace CodebaseMcpServer.Domain.IndexManagement.Events
{
    public record IndexLibraryCreated(
        IndexLibraryId LibraryId,
        CodebasePath CodebasePath,
        CollectionName CollectionName
    ) : DomainEventBase;
}