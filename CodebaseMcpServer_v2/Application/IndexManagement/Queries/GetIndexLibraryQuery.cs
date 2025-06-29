using CodebaseMcpServer.Application.Shared;
using CodebaseMcpServer.Application.IndexManagement.DTOs;

namespace CodebaseMcpServer.Application.IndexManagement.Queries
{
    public record GetIndexLibraryQuery(
        int LibraryId
    ) : IQuery<IndexLibraryDto>;
}