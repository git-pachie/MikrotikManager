using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Netora.Application.Auth;
using Netora.Application.Logging;
using Netora.WebUI.Infrastructure;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

public sealed class AccountController(INetoraApiClient apiClient, ILogger<AccountController> logger, IActivityLog activity) : Controller
{
    public const string DisplayNameClaim = SessionSignIn.DisplayNameClaim;

    [AllowAnonymous]
    [HttpGet("/login")]
    public IActionResult Login(string? returnUrl = null, bool expired = false)
    {
        if (User.Identity?.IsAuthenticated == true) return LocalRedirect(SafeReturnUrl(returnUrl));
        return View(new LoginViewModel { ReturnUrl = returnUrl, SessionExpired = expired });
    }

    [AllowAnonymous]
    [HttpPost("/login")]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            model.Password = string.Empty;
            return View(model);
        }

        var result = await apiClient.LoginAsync(new LoginRequest(model.UserName.Trim(), model.Password), cancellationToken);
        model.Password = string.Empty;

        // The user name exactly as typed (cleaned and capped when written, 18.04); the password never.
        activity.Write(result.Success && result.Data is not null
            ? new ActivityEvent(ActivityCategories.Authentication, "LoginSucceeded", ActivityOutcomes.Success,
                Actor: model.UserName)
            : new ActivityEvent(ActivityCategories.Authentication, "LoginFailed", ActivityOutcomes.Failure,
                Detail: result.Error?.Code ?? "no_response", Level: ActivityLevel.Warning, Actor: model.UserName));

        if (!result.Success || result.Data is null)
        {
            ModelState.AddModelError(string.Empty, result.Error?.Code switch
            {
                "invalid_credentials" => "Invalid user name or password.",
                "locked_out" => "This account is temporarily locked. Try again later.",
                "login_rate_limited" => "Too many sign-in attempts. Wait a minute and try again.",
                "validation_failed" => "Enter your user name and password.",
                _ => "Sign-in is unavailable right now. Try again in a moment.",
            });
            return View(model);
        }

        await SessionSignIn.SignInAsync(HttpContext, result.Data);
        if (result.Data.User.MustChangePassword) return Redirect("/profile/password");
        return LocalRedirect(SafeReturnUrl(model.ReturnUrl));
    }

    [HttpPost("/logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        try
        {
            await apiClient.LogoutAsync(cancellationToken);
        }
        catch (ApiUnauthorizedException)
        {
            // Token already invalid on the API side; the browser session still has to end.
            logger.LogInformation("Logout called with a token the API no longer accepts");
        }

        activity.Write(new ActivityEvent(ActivityCategories.Authentication, "Logout", ActivityOutcomes.Success));
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("/access-denied")]
    public IActionResult AccessDenied() => View();

    private string SafeReturnUrl(string? returnUrl)
        => !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/dashboard";
}
