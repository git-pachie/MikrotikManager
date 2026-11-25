using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Netora.Application.Auth;
using Netora.WebUI.Services;

namespace Netora.WebUI.Infrastructure;

/// <summary>Creates the browser session (auth cookie) from an API login result. Used by sign-in and password change.</summary>
public static class SessionSignIn
{
    public const string DisplayNameClaim = "display_name";
    public const string MustChangePasswordClaim = "must_change_password";
    public const string ThemeClaim = "theme";

    public static async Task SignInAsync(HttpContext http, LoginResultDto login)
    {
        var user = login.User;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Role, user.Role),
            new(DisplayNameClaim, user.DisplayName),
        };
        if (user.MustChangePassword) claims.Add(new Claim(MustChangePasswordClaim, "true"));
        if (!string.IsNullOrEmpty(user.Theme)) claims.Add(new Claim(ThemeClaim, user.Theme));

        // The cookie lives exactly as long as the API token: a session cookie whose ticket expires with the token.
        var properties = new AuthenticationProperties { IsPersistent = false, AllowRefresh = false, ExpiresUtc = login.ExpiresAtUtc };
        properties.StoreTokens([new AuthenticationToken { Name = BearerTokenHandler.AccessTokenName, Value = login.AccessToken }]);

        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)), properties);
    }

    /// <summary>Re-issues the current cookie with some claims replaced, keeping the same API token and expiry.</summary>
    public static async Task UpdateClaimsAsync(HttpContext http, Action<List<Claim>> change)
    {
        var current = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!current.Succeeded || current.Principal is null || current.Properties is null) return;
        var claims = current.Principal.Claims.ToList();
        change(claims);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)), current.Properties);
    }
}

/// <summary>
/// While the account has a temporary password, every page except My Profile and sign-out redirects to the password
/// page. The API enforces the same rule independently (403 password_change_required).
/// </summary>
public sealed class PasswordChangeRequiredFilter : Microsoft.AspNetCore.Mvc.Filters.IActionFilter
{
    private static readonly string[] AllowedControllers = ["Account", "Profile", "Home"];

    public void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
    {
        if (!context.HttpContext.User.HasClaim(SessionSignIn.MustChangePasswordClaim, "true")) return;
        var controller = context.RouteData.Values["controller"] as string;
        if (AllowedControllers.Contains(controller, StringComparer.OrdinalIgnoreCase)) return;
        context.Result = new Microsoft.AspNetCore.Mvc.RedirectResult("/profile/password");
    }

    public void OnActionExecuted(Microsoft.AspNetCore.Mvc.Filters.ActionExecutedContext context) { }
}
