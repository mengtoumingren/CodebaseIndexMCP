using System;
using System.Linq;
using System.Collections.Generic;
using CodebaseMcpServer.Domain.Shared;

namespace CodebaseMcpServer.Domain.SemanticSearch.ValueObjects
{
    public class EmbeddingVector : ValueObject
    {
        public float[] Values { get; }

        public EmbeddingVector(float[] values)
        {
            if (values == null || values.Length == 0)
                throw new ArgumentException("Embedding vector cannot be empty", nameof(values));
            Values = values;
        }
        
        public double CosineSimilarity(EmbeddingVector other)
        {
            if (Values.Length != other.Values.Length)
                throw new ArgumentException("Vector dimensions must match");
                
            var dotProduct = Values.Zip(other.Values, (a, b) => a * b).Sum();
            var magnitudeA = Math.Sqrt(Values.Sum(x => x * x));
            var magnitudeB = Math.Sqrt(other.Values.Sum(x => x * x));
            
            return dotProduct / (magnitudeA * magnitudeB);
        }

        protected override IEnumerable<object> GetEqualityComponents()
        {
            foreach (var value in Values)
            {
                yield return value;
            }
        }
    }
}