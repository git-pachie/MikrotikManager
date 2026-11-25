using Microsoft.AspNetCore.Mvc;
using Netora.Application.Settings;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

/// <summary>Settings (13.02). Managers only; the API enforces the same.</summary>
public class SettingsController(INetoraApiClient apiClient) : Controller
{
    private const string TestResultKey = "TestEmailResult";

    [HttpGet("/settings")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        var model = await LoadAsync(cancellationToken);
        if (TempData[TestResultKey] is string test)
            model.TestResult = System.Text.Json.JsonSerializer.Deserialize<TestEmailResultDto>(test);
        return View(model);
    }

    [HttpPost("/settings/router")]
    public async Task<IActionResult> Router([Bind(Prefix = "Router")] RouterSettingsForm form, CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        if (ModelState.IsValid)
        {
            var result = await apiClient.UpdateRouterSettingsAsync(
                new UpdateRouterSettingsRequest(form.Name, form.Address, form.Port, form.UserName, form.NewPassword, form.ClearPassword, form.CertificateThumbprint), cancellationToken);
            if (result.Success)
            {
                TempData[CustomersController.ToastKey] = "Router settings saved.";
                return Redirect("/settings");
            }
            ApiErrors.Apply(ModelState, result.Error, typeof(RouterSettingsForm), "The router settings were not saved.", "Router.");
        }
        var model = await LoadAsync(cancellationToken);
        form.NewPassword = null;
        model.Router = form;
        return View("Index", model);
    }

    /// <summary>Test Connection: reads the router identity with the saved settings.</summary>
    [HttpPost("/settings/router/test")]
    public async Task<IActionResult> TestRouter(CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        var result = await apiClient.TestRouterAsync(cancellationToken);
        if (result.Data is { } r)
            TempData[r.Connected ? CustomersController.ToastKey : CustomersController.ErrorKey] = (r.Mode == "Mock" ? "Mock router: " : "") + r.Message;
        else
            TempData[CustomersController.ErrorKey] = "The connection was not tested. " + (result.Error?.Message ?? "Unexpected error.");
        return Redirect("/settings");
    }

    [HttpPost("/settings/email")]
    public async Task<IActionResult> Email([Bind(Prefix = "Email")] EmailSettingsForm form, CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        if (ModelState.IsValid)
        {
            var result = await apiClient.UpdateEmailSettingsAsync(
                new UpdateEmailSettingsRequest(form.Enabled, form.From, form.FromName, form.AppBaseUrl), cancellationToken);
            if (result.Success)
            {
                TempData[CustomersController.ToastKey] = form.Enabled ? "Email settings saved. Approval emails are on." : "Email settings saved. Approval emails are off.";
                return Redirect("/settings");
            }
            ApiErrors.Apply(ModelState, result.Error, typeof(EmailSettingsForm), "The email settings were not saved.", "Email.");
        }
        var model = await LoadAsync(cancellationToken);
        model.Email = form;
        return View("Index", model);
    }

    [HttpPost("/settings/email/test")]
    public async Task<IActionResult> TestEmail(CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        var result = await apiClient.SendTestEmailAsync(cancellationToken);
        if (result.Success && result.Data is not null)
            TempData[TestResultKey] = System.Text.Json.JsonSerializer.Serialize(result.Data);
        else
            TempData[CustomersController.ErrorKey] = "No test email was sent. " + (result.Error?.Message ?? "Unexpected error.");
        return Redirect("/settings#notifications");
    }

    private async Task<SettingsViewModel> LoadAsync(CancellationToken cancellationToken)
    {
        var result = await apiClient.GetSettingsAsync(cancellationToken);
        if (result.Data is not { } d)
            return new SettingsViewModel { ErrorMessage = "Settings could not be loaded. The Web API is unavailable; try again in a moment." };
        return new SettingsViewModel
        {
            Data = d,
            Router = new RouterSettingsForm
            {
                Name = d.Router.Name, Address = d.Router.Address, Port = d.Router.Port, UserName = d.Router.UserName,
                CertificateThumbprint = d.Router.CertificateThumbprint,
            },
            Email = new EmailSettingsForm { Enabled = d.Email.Enabled, From = d.Email.From, FromName = d.Email.FromName, AppBaseUrl = d.Email.AppBaseUrl },
        };
    }

    private bool IsManager => User.IsInRole("Manager");

    private ViewResult Denied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View("~/Views/Account/AccessDenied.cshtml");
    }
}
