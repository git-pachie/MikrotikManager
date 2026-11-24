namespace Netora.Application.Common;

/// <summary>Error details returned inside <see cref="ApiResponse{T}"/>.</summary>
public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? Details = null);

/// <summary>Consistent envelope for every Web API response (usable by web and future mobile clients).</summary>
public sealed record ApiResponse<T>(bool Success, T? Data, ApiError? Error, string? TraceId)
{
    public static ApiResponse<T> Ok(T data, string? traceId = null) => new(true, data, null, traceId);

    public static ApiResponse<T> Fail(string code, string message, string? traceId = null,
        IReadOnlyDictionary<string, string[]>? details = null)
        => new(false, default, new ApiError(code, message, details), traceId);
}
