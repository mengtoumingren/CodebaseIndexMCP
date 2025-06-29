using System;
using CodebaseMcpServer.Domain.Shared;

namespace CodebaseMcpServer.Domain.TaskManagement.ValueObjects
{
    public record TaskId
    {
        public Guid Value { get; init; }

        public TaskId(Guid value)
        {
            if (value == Guid.Empty)
                throw new ArgumentException("Task ID cannot be empty", nameof(value));
            Value = value;
        }

        public static TaskId New() => new(Guid.NewGuid());
        
        public static implicit operator Guid(TaskId id) => id.Value;
        public static implicit operator TaskId(Guid value) => new(value);
    }
}