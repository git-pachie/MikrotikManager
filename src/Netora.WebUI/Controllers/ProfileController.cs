using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Users;
using Netora.WebUI.Infrastructure;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

/// <summary>My Profile: every signed-in user edits their own name, email, theme and password.</summary>
public class ProfileController(INetoraApiClient apiClient) : Controller
{
    [HttpGet("/profile")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var profile = await LoadAsync(cancellationToken);
        return profile is null ? Unavailable() : View(profile);
    }

    [HttpPost("/profile")]
    public async Task<IActionResult> Index(ProfileViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await apiClient.UpdateProfileAsync(new UpdateProfileRequest(model.DisplayName, model.Email), cancellationToken);
            if (result.Success)
            {
                await SessionSignIn.UpdateClaimsAsync(HttpContext, claims =>
                {
                    claims.RemoveAll(c => c.Type == SessionSignIn.DisplayNameClaim);
                    claims.Add(new Claim(SessionSignIn.DisplayNameClaim, model.DisplayName.Trim()));
                });
                TempData[CustomersController.ToastKey] = result.Data!.Changed ? "Profile saved." : "No changes to save.";
                return Redirect("/profile");
            }
            ApiErrors.Apply(ModelState, result.Error, typeof(ProfileViewModel), "Your profile was not saved.");
        }
        var current = await LoadAsync(cancellationToken);
        if (current is null) return Unavailable();
        model.UserName = current.UserName;
        model.Role = current.Role;
        model.Theme = current.Theme;
        model.MustChangePassword = current.MustChangePassword;
        return View(model);
    }

    [HttpGet("/profile/password")]
    public IActionResult Password()
        => View(new ChangePasswordViewModel { Forced = User.HasClaim(SessionSignIn.MustChangePasswordClaim, "true") });

    [HttpPost("/profile/password")]
    public async Task<IActionResult> Password(ChangePasswordViewModel model, CancellationToken cancellationToken)
    {
        model.Forced = User.HasClaim(SessionSignIn.MustChangePasswordClaim, "true");
        if (ModelState.IsValid)
        {
            var result = await apiClient.ChangePasswordAsync(new ChangePasswordRequest(model.CurrentPassword, model.NewPassword), cancellationToken);
            if (result.Success && result.Data is not null)
            {
                // The old API token stopped working when the password changed; continue the session with the new one.
                await SessionSignIn.SignInAsync(HttpContext, result.Data);
                TempData[CustomersController.ToastKey] = "Password changed.";
                return Redirect(model.Forced ? "/customers" : "/profile");
            }
            ApiErrors.Apply(ModelState, result.Error, typeof(ChangePasswordViewModel), "Your password was not changed.");
        }
        model.CurrentPassword = model.NewPassword = model.ConfirmPassword = string.Empty;
        return View(model);
    }

    /// <summary>Saves the theme chosen with the top bar switch. Called from site.js; answers 204.</summary>
    [HttpPost("/profile/theme")]
    public async Task<IActionResult> Theme(string? theme, CancellationToken cancellationToken)
    {
        if (theme is not ("light" or "dark")) return BadRequest();
        var result = await apiClient.SetThemeAsync(new ThemeRequest(theme), cancellationToken);
        if (!result.Success) return StatusCode(StatusCodes.Status502BadGateway);
        await SessionSignIn.UpdateClaimsAsync(HttpContext, claims =>
        {
            claims.RemoveAll(c => c.Type == SessionSignIn.ThemeClaim);
            claims.Add(new Claim(SessionSignIn.ThemeClaim, theme));
        });
        return NoContent();
    }

    private async Task<ProfileViewModel?> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await apiClient.GetProfileAsync(cancellationToken);
        return result.Data is not { } p ? null : new ProfileViewModel
        {
            UserName = p.UserName, Role = p.Role, DisplayName = p.DisplayName, Email = p.Email, Theme = p.ThemePreference,
            MustChangePassword = p.MustChangePassword,
        };
    }

    private IActionResult Unavailable()
    {
        TempData[CustomersController.ErrorKey] = "Your profile could not be loaded. The Web API is unavailable; try again in a moment.";
        return Redirect("/customers");
    }
}
