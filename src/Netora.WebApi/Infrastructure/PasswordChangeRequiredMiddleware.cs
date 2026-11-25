using Netora.Application.Common;

namespace Netora.WebApi.Infrastructure;

/// <summary>
/// While an account has a temporary password (new account or reset by a Manager), only the profile, sign-out and
/// health endpoints answer; everything else returns 403 <c>password_change_required</c> (13.01).
/// </summary>
public sealed class PasswordChangeRequiredMiddleware(RequestDelegate next)
{
    public const string ClaimType = "pwd_change";

    private static readonly string[] AllowedPrefixes = ["/api/v1/profile", "/api/v1/auth", "/api/v1/health"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.HasClaim(ClaimType, "true")
            && !AllowedPrefixes.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail("password_change_required",
                "Choose a new password before continuing.", context.TraceIdentifier));
            return;
        }
        await next(context);
    }
}
