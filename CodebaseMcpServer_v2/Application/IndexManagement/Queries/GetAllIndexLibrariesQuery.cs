using System.Collections.Generic;
using CodebaseMcpServer.Application.Shared;
using CodebaseMcpServer.Application.IndexManagement.DTOs;

namespace CodebaseMcpServer.Application.IndexManagement.Queries
{
    public record GetAllIndexLibrariesQuery() : IQuery<IReadOnlyList<IndexLibraryDto>>;
}