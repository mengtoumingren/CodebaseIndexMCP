using System;
using System.Collections.Generic;
using System.Linq;
using CodebaseMcpServer.Domain.Shared;

namespace CodebaseMcpServer.Domain.IndexManagement.ValueObjects
{
    public class WatchConfiguration : ValueObject
    {
        public bool IsEnabled { get; }
        public IReadOnlyList<string> IncludePatterns { get; }
        public IReadOnlyList<string> ExcludePatterns { get; }
        public TimeSpan DebounceInterval { get; }
        
        public WatchConfiguration(
            bool isEnabled,
            IEnumerable<string>? includePatterns = null,
            IEnumerable<string>? excludePatterns = null,
            TimeSpan? debounceInterval = null)
        {
            IsEnabled = isEnabled;
            IncludePatterns = includePatterns?.ToList() ?? new List<string> { "*.*" };
            ExcludePatterns = excludePatterns?.ToList() ?? new List<string>();
            DebounceInterval = debounceInterval ?? TimeSpan.FromSeconds(1);
            
            if (DebounceInterval < TimeSpan.Zero)
                throw new ArgumentException("Debounce interval cannot be negative");
        }
        
        public static WatchConfiguration Disabled => new(false);
        
        public IndexConfiguration ToIndexConfiguration()
        {
            return new IndexConfiguration(IncludePatterns, ExcludePatterns, IsEnabled);
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return IsEnabled;
            foreach (var pattern in IncludePatterns)
                yield return pattern;
            foreach (var pattern in ExcludePatterns)
                yield return pattern;
            yield return DebounceInterval;
        }
    }
}