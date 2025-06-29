using CodebaseMcpServer.Domain.IndexManagement.ValueObjects;

namespace CodebaseMcpServer.Domain.IndexManagement.Services
{
    public interface ICollectionNameGenerator
    {
        CollectionName GenerateFor(CodebasePath path);
        bool IsUnique(CollectionName name);
    }
}