namespace Academies.BuildingBlocks.Application.Models;

/// <summary>Envelope for every API response, success or failure.</summary>
public class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public IReadOnlyDictionary<string, string[]>? Errors { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors };
}

/// <summary>Response without a payload.</summary>
public sealed class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok(string? message = null) => new() { Success = true, Message = message };

    public static new ApiResponse Fail(string message, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors };
}
