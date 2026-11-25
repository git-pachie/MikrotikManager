using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Netora.Application.Common;
using Netora.Application.Logging;

namespace Netora.WebApi.Infrastructure;

/// <summary>
/// Writes 401 and 403 results in the standard envelope instead of an empty body, and logs authorization denials
/// (18.02): every 403, and a 401 only when a token was sent (an anonymous call is not a denial worth logging).
/// </summary>
public sealed class ApiAuthorizationResultHandler(IActivityLog activity) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            if (!string.IsNullOrEmpty(context.Request.Headers.Authorization))
                LogDenied(context, "TokenRejected");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(
                ApiResponse<object>.Fail("unauthorized", "Authentication is required.", context.TraceIdentifier));
            return;
        }

        if (authorizeResult.Forbidden)
        {
            LogDenied(context, "Forbidden");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                ApiResponse<object>.Fail("forbidden", "You do not have permission to perform this action.", context.TraceIdentifier));
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    /// <summary>Method and path only; the query string can hold search text.</summary>
    private void LogDenied(HttpContext context, string reason)
        => activity.Write(new ActivityEvent(ActivityCategories.Application, "AuthorizationDenied", ActivityOutcomes.Denied,
            $"{context.Request.Method} {context.Request.Path}", reason, ActivityLevel.Warning));
}
