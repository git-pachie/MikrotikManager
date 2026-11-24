using Microsoft.AspNetCore.Diagnostics;
using Netora.Application.Common;

namespace Netora.WebApi.Infrastructure;

/// <summary>Turns unhandled exceptions into the standard envelope without leaking internals.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        logger.LogError(exception, "Unhandled exception for {Method} {Path}",
            httpContext.Request.Method, httpContext.Request.Path);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail("server_error", "An unexpected error occurred.", httpContext.TraceIdentifier),
            cancellationToken);
        return true;
    }
}
