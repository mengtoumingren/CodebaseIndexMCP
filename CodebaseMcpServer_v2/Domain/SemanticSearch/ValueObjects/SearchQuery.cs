using System;
using System.Collections.Generic;
using CodebaseMcpServer.Domain.Shared;

namespace CodebaseMcpServer.Domain.SemanticSearch.ValueObjects
{
    public class SearchQuery : ValueObject
    {
        public string Query { get; }
        public int MaxResults { get; }

        public SearchQuery(string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ArgumentException("Search query cannot be empty", nameof(query));
            if (maxResults <= 0 || maxResults > 100)
                throw new ArgumentException("Max results must be between 1 and 100", nameof(maxResults));

            Query = query;
            MaxResults = maxResults;
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            yield return Query;
            yield return MaxResults;
        }
    }
}