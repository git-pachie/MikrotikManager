using Microsoft.AspNetCore.Diagnostics;
using Netora.Application.Common;

namespace Netora.WebApi.Infrastructure;

/// <summary>
/// Turns unhandled exceptions into the standard envelope without leaking internals. The exception itself is logged
/// once, as an UnhandledException event, by UnhandledExceptionLoggingMiddleware (module 18).
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail("server_error", "An unexpected error occurred.", httpContext.TraceIdentifier),
            cancellationToken);
        return true;
    }
}
