namespace CodebaseMcpServer.Domain.Shared
{
    public abstract class Entity<TId>
    {
        public TId? Id { get; protected set; }
        
        protected Entity(TId id)
        {
            Id = id;
        }
        
        protected Entity() { Id = default!; } // For EF Core
        
        public override bool Equals(object? obj)
        {
            if (obj is not Entity<TId> other)
                return false;
                
            if (ReferenceEquals(this, other))
                return true;
                
            if (Id == null || other.Id == null) // Handle null Ids
                return false;

            return Id.Equals(other.Id);
        }
        
        public override int GetHashCode()
        {
            return Id?.GetHashCode() ?? 0;
        }
    }
}