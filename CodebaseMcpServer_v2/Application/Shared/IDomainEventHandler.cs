using System.Threading;
using System.Threading.Tasks;
using CodebaseMcpServer.Domain.Shared;

namespace CodebaseMcpServer.Application.Shared
{
    public interface IDomainEventHandler<in TDomainEvent>
        where TDomainEvent : IDomainEvent
    {
        Task HandleAsync(TDomainEvent domainEvent, CancellationToken cancellationToken = default);
    }
}