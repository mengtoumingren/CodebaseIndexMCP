using System;
using System.Collections.Generic;

namespace CodebaseMcpServer.Application.Shared
{
    public class Result
    {
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public string? Error { get; }
        public IReadOnlyList<string>? Errors { get; }
        
        protected Result(bool isSuccess, string? error, IReadOnlyList<string>? errors = null)
        {
            IsSuccess = isSuccess;
            Error = error;
            Errors = errors ?? new List<string>();
        }
        
        public static Result Success() => new(true, null);
        public static Result Failure(string error) => new(false, error);
        public static Result Failure(IReadOnlyList<string> errors) => new(false, null, errors);
        
        public static Result<T> Success<T>(T value) => new(value, true, null);
        public static Result<T> Failure<T>(string error) => new(default!, false, error);
        public static Result<T> Failure<T>(IReadOnlyList<string> errors) => new(default!, false, null, errors);
    }
    
    public class Result<T> : Result
    {
        public T? Value { get; }
        
        internal Result(T? value, bool isSuccess, string? error, IReadOnlyList<string>? errors = null)
            : base(isSuccess, error, errors)
        {
            Value = value;
        }
    }
}