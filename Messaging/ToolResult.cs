using System.Diagnostics.CodeAnalysis;

namespace TM_GenericMapping.Messaging;


public readonly record struct None
{
    public static readonly None Value = default;
}
public readonly record struct ToolFailure
{
    public required string ToolId { get; init; }
    public required string ErrorCode { get; init; }
    public object? Data { get; init; }
}
public readonly record struct ToolSuccess
{
    public required string ToolId { get; init; }
}

public readonly record struct ToolResult<T>
{
    public required ToolOutcome Outcome { get; init; }
    public required string ToolId { get; init; }

    public T Value { get; init; }
    public string? ErrorCode { get; init; }
    public object? Data { get; init; }


    public bool IsSuccess => Outcome == ToolOutcome.Success;


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