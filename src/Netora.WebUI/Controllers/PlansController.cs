using Microsoft.AspNetCore.Mvc;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

/// <summary>Internet Plans (05, plans.html). Everyone sees the catalogue; Managers add, edit and deactivate. The API enforces the same.</summary>
public class PlansController(INetoraApiClient apiClient) : Controller
{
    [HttpGet("/plans")]
    public async Task<IActionResult> Index(string? sortBy, string? sortDir, CancellationToken cancellationToken)
    {
        var column = Netora.Application.Catalog.PlanSort.Columns.Contains(sortBy) ? sortBy : null;
        var descending = column is not null && string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);
        var result = await apiClient.GetPlansAsync(cancellationToken, column, descending);
        return View(new PlanListViewModel
        {
            Data = result.Data,
            IsManager = IsManager,
            SortBy = column,
            Descending = descending,
            ErrorMessage = result.Success ? null : "Plans could not be loaded. The Web API is unavailable; try again in a moment.",
        });
    }

    /// <summary>Reads the router's PPP profiles and shows which plans' profiles exist. Changes nothing.</summary>
    [HttpGet("/plans/router-check")]
    public async Task<IActionResult> RouterCheck(CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        var plans = await apiClient.GetPlansAsync(cancellationToken);
        var check = await apiClient.CheckPlanProfilesAsync(cancellationToken);
        return View("Index", new PlanListViewModel
        {
            Data = plans.Data,
            IsManager = true,
            ErrorMessage = plans.Success ? null : "Plans could not be loaded. The Web API is unavailable; try again in a moment.",
            Check = check.Data,
            CheckError = check.Success ? null : "The router check could not be run. " + (check.Error?.Message ?? "Unexpected error."),
        });
    }

    /// <summary>
    /// Managers only: creates the plan's PPP profile on the router, or sets its rate-limit to the plan's speeds.
    /// Then shows the router check, so the result is visible at once.
    /// </summary>
    [HttpPost("/plans/{id:int}/send-to-router")]
    public async Task<IActionResult> SendToRouter(int id, CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        var result = await apiClient.SendPlanToRouterAsync(id, cancellationToken);
        if (result.Data is { } sent)
        {
            var where = sent.Mode == "Mock" ? "the mock router" : "the router";
            TempData[CustomersController.ToastKey] = sent.Action switch
            {
                "Created" => $"PPP profile {sent.Profile} created on {where} with rate-limit {sent.RateLimit}.",
                "Updated" => $"PPP profile {sent.Profile} updated on {where}: rate-limit {sent.PreviousRateLimit ?? "unlimited"} → {sent.RateLimit}. Customers on this profile get the new speed when they reconnect.",
                _ => $"PPP profile {sent.Profile} on {where} already has rate-limit {sent.RateLimit}. Nothing changed.",
            };
        }
        else
        {
            if (result.Error?.Code == "not_found") return NotFoundPage();
            TempData[CustomersController.ErrorKey] = "The profile was not sent. " + (result.Error?.Code switch
            {
                "validation_failed" => string.Join(" ", result.Error.Details?.SelectMany(d => d.Value) ?? []),
                "api_unreachable" => "The Web API is unavailable; try again in a moment.",
                _ => result.Error?.Message ?? "Unexpected error.",
            });
        }
        return Redirect("/plans/router-check");
    }

    /// <summary>Add Internet Plan, optionally filled in from an existing plan (Copy). The copy is a new plan; nothing is saved yet.</summary>
    [HttpGet("/plans/new")]
    public async Task<IActionResult> Create(int? copyFrom, CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        if (copyFrom is null) return View("Form", new PlanFormViewModel());

        var source = await apiClient.GetPlanAsync(copyFrom.Value, cancellationToken);
        if (source.Data is not { } p)
        {
            if (source.Error?.Code == "not_found") return NotFoundPage();
            TempData[CustomersController.ErrorKey] = "The plan to copy could not be loaded. " + source.Error?.Message;
            return Redirect("/plans");
        }
        // Name and profile are kept as they are, so the Manager must give the copy its own (both are checked on save).
        return View("Form", new PlanFormViewModel
        {
            Name = $"{p.Name} (copy)", MonthlyFee = p.MonthlyFee, DownloadMbps = p.DownloadMbps, UploadMbps = p.UploadMbps,
            RouterProfile = p.RouterProfile, IsActive = p.IsActive, CopiedFrom = p.Name,
        });
    }

    [HttpPost("/plans/new")]
    public async Task<IActionResult> Create(PlanFormViewModel model, CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        model.Id = null;
        if (!ModelState.IsValid) return View("Form", model);

        var result = await apiClient.CreatePlanAsync(model.ToRequest(), cancellationToken);
        if (result.Success)
        {
            TempData[CustomersController.ToastKey] = $"Plan {model.Name.Trim()} added.";
            return Redirect("/plans");
        }
        ApiErrors.Apply(ModelState, result.Error, typeof(PlanFormViewModel), "The plan was not saved.");
        return View("Form", model);
    }

    [HttpGet("/plans/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        var result = await apiClient.GetPlanAsync(id, cancellationToken);
        if (result.Data is { } p) return View("Form", PlanFormViewModel.From(p));
        if (result.Error?.Code == "not_found") return NotFoundPage();
        TempData[CustomersController.ErrorKey] = "The plan could not be loaded. " + result.Error?.Message;
        return Redirect("/plans");
    }

    [HttpPost("/plans/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, PlanFormViewModel model, CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        model.Id = id;
        if (ModelState.IsValid)
        {
            var result = await apiClient.UpdatePlanAsync(id, model.ToRequest(), cancellationToken);
            if (result.Success)
            {
                TempData[CustomersController.ToastKey] = result.Data!.Changed ? "Plan saved." : "No changes to save.";
                return Redirect("/plans");
            }
            switch (result.Error?.Code)
            {
                case "not_found":
                    return NotFoundPage();
                case "concurrency_conflict":
                    ModelState.AddModelError(string.Empty,
                        "This plan was changed by someone else while you were editing. Reload the page to see the latest values; your edits were not saved.");
                    break;
                default:
                    ApiErrors.Apply(ModelState, result.Error, typeof(PlanFormViewModel), "The plan was not saved.");
                    break;
            }
        }

        // Show the saved profile and subscriber count again, as on first load.
        var current = await apiClient.GetPlanAsync(id, cancellationToken);
        if (current.Data is { } saved)
        {
            model.Subscribers = saved.Subscribers;
            model.SavedRouterProfile = saved.RouterProfile;
        }
        return View("Form", model);
    }

    private bool IsManager => User.IsInRole("Manager");

    private ViewResult Denied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View("~/Views/Account/AccessDenied.cshtml");
    }

    private ViewResult NotFoundPage()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        ViewData["NotFoundTitle"] = "Plan not found";
        return View("NotFound");
    }
}
