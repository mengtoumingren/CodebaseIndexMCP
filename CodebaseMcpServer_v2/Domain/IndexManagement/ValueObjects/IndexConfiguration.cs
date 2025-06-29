using System;
using System.Collections.Generic;
using System.Linq;
using CodebaseMcpServer.Domain.Shared;

namespace CodebaseMcpServer.Domain.IndexManagement.ValueObjects
{
    public class IndexConfiguration : ValueObject
    {
        public IReadOnlyList<string> IncludePatterns { get; }
        public IReadOnlyList<string> ExcludePatterns { get; }
        public bool EnableFileWatching { get; }

        public IndexConfiguration(
            IEnumerable<string>? includePatterns = null,
            IEnumerable<string>? excludePatterns = null,
            bool enableFileWatching = false)
        {
            IncludePatterns = includePatterns?.ToList() ?? new List<string> { "*.*" };
            ExcludePatterns = excludePatterns?.ToList() ?? new List<string>();
            EnableFileWatching = enableFileWatching;
        }

        public static IndexConfiguration Default => new(new List<string> { "*.*" }, new List<string>(), false);

        protected override IEnumerable<object> GetEqualityComponents()
        {
            foreach (var pattern in IncludePatterns)
                yield return pattern;
            foreach (var pattern in ExcludePatterns)
                yield return pattern;
            yield return EnableFileWatching;
        }
    }
}