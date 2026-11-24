using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Netora.Application.Auth;
using Netora.Infrastructure.Identity;

namespace Netora.WebApi.Infrastructure;

public static class AuthenticationSetup
{
    public static IServiceCollection AddNetoraAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configure after DI is built so the validated JwtOptions are the single source of truth.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = NetoraClaimTypes.Name,
                    RoleClaimType = NetoraClaimTypes.Role,
                };
                bearer.Events = new JwtBearerEvents { OnTokenValidated = RevalidateAgainstDatabaseAsync };
            });

        services.AddAuthorizationBuilder()
            // Every endpoint requires a signed-in user unless it opts out with [AllowAnonymous].
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.ManagerOnly, p => p.RequireAuthenticatedUser().RequireRole(Roles.Manager));

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ApiAuthorizationResultHandler>();
        return services;
    }

    /// <summary>
    /// A valid signature is not enough. Each request re-checks the account in the database, so deactivation,
    /// lockout, password change (security stamp) and role changes take effect immediately.
    /// Roles come from the database, never from the token.
    /// </summary>
    private static async Task RevalidateAgainstDatabaseAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var userId = principal?.FindFirstValue(NetoraClaimTypes.Subject);
        var tokenStamp = principal?.FindFirstValue(NetoraClaimTypes.SecurityStamp);
        if (principal?.Identity is not ClaimsIdentity identity || string.IsNullOrEmpty(userId))
        {
            context.Fail("Invalid token.");
            return;
        }

        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive || await userManager.IsLockedOutAsync(user)
            || !string.Equals(user.SecurityStamp, tokenStamp, StringComparison.Ordinal))
        {
            context.Fail("Account is no longer valid.");
            return;
        }

        var roles = (await userManager.GetRolesAsync(user)).Where(r => Roles.All.Contains(r)).ToList();
        foreach (var existing in identity.FindAll(NetoraClaimTypes.Role).ToList())
            identity.RemoveClaim(existing);
        foreach (var role in roles)
            identity.AddClaim(new Claim(NetoraClaimTypes.Role, role));
    }
}
