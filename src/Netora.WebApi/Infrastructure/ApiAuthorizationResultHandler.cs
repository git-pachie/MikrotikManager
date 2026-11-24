using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Netora.Application.Common;

namespace Netora.WebApi.Infrastructure;

/// <summary>Writes 401 and 403 results in the standard envelope instead of an empty body.</summary>
public sealed class ApiAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Challenged)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(
                ApiResponse<object>.Fail("unauthorized", "Authentication is required.", context.TraceIdentifier));
            return;
        }

        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(
                ApiResponse<object>.Fail("forbidden", "You do not have permission to perform this action.", context.TraceIdentifier));
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
