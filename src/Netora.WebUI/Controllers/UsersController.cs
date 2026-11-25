using Microsoft.AspNetCore.Mvc;
using Netora.Application.Users;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

/// <summary>User Management (13.01). Managers only; the API enforces the same.</summary>
public class UsersController(INetoraApiClient apiClient) : Controller
{
    [HttpGet("/users")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        var result = await apiClient.GetUsersAsync(cancellationToken);
        ViewData["Error"] = result.Success ? null : "Users could not be loaded. The Web API is unavailable; try again in a moment.";
        return View(result.Data);
    }

    [HttpGet("/users/new")]
    public IActionResult Create() => IsManager ? View("Form", new UserFormViewModel()) : Denied();

    [HttpPost("/users/new")]
    public async Task<IActionResult> Create(UserFormViewModel model, CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        if (string.IsNullOrEmpty(model.TemporaryPassword))
            ModelState.AddModelError(nameof(model.TemporaryPassword), "Enter a temporary password.");
        if (!ModelState.IsValid) return FormAgain(model);

        var result = await apiClient.CreateUserAsync(
            new CreateUserRequest(model.UserName, model.DisplayName, model.Email, model.Role, model.TemporaryPassword!), cancellationToken);
        if (result.Success)
        {
            TempData[CustomersController.ToastKey] = $"User {model.UserName.Trim()} created. Give them the temporary password; they must choose a new one at first sign-in.";
            return Redirect("/users");
        }
        ApiErrors.Apply(ModelState, result.Error, typeof(UserFormViewModel), "The user was not created.");
        return FormAgain(model);
    }

    [HttpGet("/users/{id}/edit")]
    public async Task<IActionResult> Edit(string id, CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        var result = await apiClient.GetUserAsync(id, cancellationToken);
        if (result.Data is not { } u)
        {
            if (result.Error?.Code == "not_found") return NotFoundPage();
            TempData[CustomersController.ErrorKey] = "The user could not be loaded. " + result.Error?.Message;
            return Redirect("/users");
        }
        return View("Form", new UserFormViewModel
        {
            Id = u.Id, UserName = u.UserName, DisplayName = u.DisplayName, Email = u.Email, Role = u.Role, IsActive = u.IsActive,
            ConcurrencyStamp = u.ConcurrencyStamp, IsCurrentUser = u.IsCurrentUser, IsLockedOut = u.IsLockedOut,
            MustChangePassword = u.MustChangePassword, CreatedAtUtc = u.CreatedAtUtc, LastSignInAtUtc = u.LastSignInAtUtc,
        });
    }

    [HttpPost("/users/{id}/edit")]
    public async Task<IActionResult> Edit(string id, UserFormViewModel model, CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        model.Id = id;
        ModelState.Remove(nameof(model.UserName));   // not editable
        if (!ModelState.IsValid) return await EditAgainAsync(model, cancellationToken);

        var result = await apiClient.UpdateUserAsync(id,
            new UpdateUserRequest(model.DisplayName, model.Email, model.Role, model.IsActive, model.ConcurrencyStamp ?? string.Empty), cancellationToken);
        if (result.Error?.Code == "not_found") return NotFoundPage();
        if (result.Success)
        {
            TempData[CustomersController.ToastKey] = result.Data!.Changed ? "User saved." : "No changes to save.";
            return Redirect("/users");
        }
        ApiErrors.Apply(ModelState, result.Error, typeof(UserFormViewModel), "The user was not saved.");
        return await EditAgainAsync(model, cancellationToken);
    }

    [HttpPost("/users/{id}/reset-password")]
    public async Task<IActionResult> ResetPassword(string id, string? temporaryPassword, CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        var result = await apiClient.ResetUserPasswordAsync(id, new ResetPasswordRequest(temporaryPassword ?? string.Empty), cancellationToken);
        if (result.Error?.Code == "not_found") return NotFoundPage();
        TempData[result.Success ? CustomersController.ToastKey : CustomersController.ErrorKey] = result.Success
            ? "Temporary password set. The account is unlocked, its sessions were ended, and a new password is required at next sign-in."
            : "The password was not reset. " + string.Join(" ", result.Error?.Details?.SelectMany(d => d.Value) ?? [result.Error?.Message ?? "Unexpected error."]);
        return Redirect($"/users/{Uri.EscapeDataString(id)}/edit");
    }

    private bool IsManager => User.IsInRole("Manager");

    private ViewResult FormAgain(UserFormViewModel model)
    {
        model.TemporaryPassword = null;
        return View("Form", model);
    }

    /// <summary>Re-reads the display-only fields so a failed save shows the current account state.</summary>
    private async Task<IActionResult> EditAgainAsync(UserFormViewModel model, CancellationToken cancellationToken)
    {
        var current = await apiClient.GetUserAsync(model.Id!, cancellationToken);
        if (current.Data is { } u)
        {
            model.UserName = u.UserName;
            model.IsCurrentUser = u.IsCurrentUser;
            model.IsLockedOut = u.IsLockedOut;
            model.MustChangePassword = u.MustChangePassword;
            model.CreatedAtUtc = u.CreatedAtUtc;
            model.LastSignInAtUtc = u.LastSignInAtUtc;
        }
        return View("Form", model);
    }

    private ViewResult Denied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View("~/Views/Account/AccessDenied.cshtml");
    }

    private ViewResult NotFoundPage()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        ViewData["NotFoundTitle"] = "User not found";
        return View("NotFound");
    }
}
