using Microsoft.AspNetCore.Http;
using Netora.Application.Logging;

namespace Netora.Logging;

/// <summary>
/// Writes one <c>UnhandledException</c> event (with the stack trace on indented lines) and rethrows, so the normal
/// error page or exception handler still runs. Place it just inside <c>UseExceptionHandler</c>.
/// </summary>
public sealed class UnhandledExceptionLoggingMiddleware(RequestDelegate next, IActivityLog activity)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (Log(context, ex))
        {
            throw; // never reached: Log returns false so the exception keeps its original stack.
        }
    }

    private bool Log(HttpContext context, Exception ex)
    {
        if (ex is OperationCanceledException && context.RequestAborted.IsCancellationRequested) return false;
        activity.Write(new ActivityEvent(ActivityCategories.Application, "UnhandledException", ActivityOutcomes.Failure,
            Target: $"{context.Request.Method} {context.Request.Path}", Level: ActivityLevel.Error, Exception: ex));
        return false;
    }
}
