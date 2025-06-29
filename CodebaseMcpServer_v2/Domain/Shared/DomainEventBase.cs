using System;

namespace CodebaseMcpServer.Domain.Shared
{
    public abstract record DomainEventBase : IDomainEvent
    {
        public DateTime OccurredOn { get; } = DateTime.UtcNow;
        public Guid EventId { get; } = Guid.NewGuid();
    }
}