using System.Threading;
using System.Threading.Tasks;

namespace CodebaseMcpServer.Application.Shared
{
    public interface IQueryHandler<in TQuery, TResult>
        where TQuery : IQuery<TResult>
    {
        Task<TResult?> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
    }
}