using System;
using System.Collections.Generic;
using CodebaseMcpServer.Application.Shared;

namespace CodebaseMcpServer.Application.IndexManagement.Commands
{
    public record UpdateWatchConfigurationCommand(
        int LibraryId,
        bool IsEnabled,
        IEnumerable<string>? IncludePatterns = null,
        IEnumerable<string>? ExcludePatterns = null,
        TimeSpan? DebounceInterval = null
    ) : ICommand<Result>;
}