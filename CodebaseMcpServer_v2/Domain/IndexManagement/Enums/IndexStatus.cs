namespace CodebaseMcpServer.Domain.IndexManagement.Enums
{
    public enum IndexStatus
    {
        Pending = 0,
        Indexing = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4
    }
}