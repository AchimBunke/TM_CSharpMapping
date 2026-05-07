using System.Diagnostics.CodeAnalysis;

namespace TM_GenericMapping.Messaging;


public interface IToolResult
{
    public ToolOutcome Outcome { get; }
    public string ToolId { get; }
    public string? ErrorCode { get; }
    public object? Data { get; }
}
public interface IToolResult<T> : IToolResult
{
    public T Value { get; }
}
public static class ToolResultExtensions
{
    extension(IToolResult toolResult)
    {
        public bool IsSuccess => toolResult.Outcome == ToolOutcome.Success;
        public bool IsFailure => toolResult.Outcome == ToolOutcome.Failure;
    }
}

public readonly record struct None
{
    public static readonly None Value = default;
}
public readonly record struct ToolFailure : IToolResult
{
    public ToolOutcome Outcome => ToolOutcome.Failure;
    public required string ToolId { get; init; }
    public required string ErrorCode { get; init; }
    public object? Data { get; init; }
}
public readonly record struct ToolSuccess : IToolResult
{
    public required string ToolId { get; init; }
    public ToolOutcome Outcome => ToolOutcome.Success;
    public string ErrorCode => string.Empty;
    public object? Data => null;
}


public readonly record struct ToolResult<T> : IToolResult<T>
{
    public required ToolOutcome Outcome { get; init; }
    public required string ToolId { get; init; }

    public T Value { get; init; }
    public string? ErrorCode { get; init; }
    public object? Data { get; init; }


    //public static implicit operator T(ToolResult<T> result) => result.Value;

    //public static implicit operator bool(ToolResult<T> result) => result.IsSuccess;

    public static implicit operator ToolResult<T>(ToolFailure failure) 
        => new() 
        { 
            Outcome = ToolOutcome.Failure, 
            ToolId = failure.ToolId, 
            ErrorCode = failure.ErrorCode, 
            Data = failure.Data 
        };

    public static implicit operator ToolResult<T>(ToolSuccess success)
        => new()
        {
            Outcome = ToolOutcome.Success,
            ToolId = success.ToolId,
        };

}

public static class ToolResult
{
    public static ToolResult<T> Success<T>(T value, string toolId)
        => new()
        {
            Outcome = ToolOutcome.Success,
            ToolId = toolId,
            Value = value
        };
    public static ToolSuccess Success(string toolId)
        => new ToolSuccess
        {
            ToolId = toolId,
        };

    public static ToolResult<T> Fail<T>(string toolId, string errorCode, object data = null!)
        => new()
        {
            Outcome = ToolOutcome.Failure,
            ToolId = toolId,
            ErrorCode = errorCode,
            Data = data
        };
    public static ToolFailure Fail(string toolId, string errorCode, object data = null!)
        => new ToolFailure
        {
            ToolId = toolId,
            ErrorCode = errorCode,
            Data = data
        };
}