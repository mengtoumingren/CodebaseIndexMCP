using System;

namespace CodebaseMcpServer.Domain.Shared
{
    public interface IDomainEvent
    {
        DateTime OccurredOn { get; }
        Guid EventId { get; }
    }
}