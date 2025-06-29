using System;

namespace CodebaseMcpServer.Application.Common.Interfaces
{
    public interface IDateTimeProvider
    {
        DateTime UtcNow { get; }
        DateTime Now { get; }
        DateOnly Today { get; }
    }
}