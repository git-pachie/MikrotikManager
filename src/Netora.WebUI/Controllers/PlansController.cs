using Microsoft.AspNetCore.Mvc;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

/// <summary>Internet Plans (05, plans.html). Everyone sees the catalogue; Managers add, edit and deactivate. The API enforces the same.</summary>
public class PlansController(INetoraApiClient apiClient) : Controller
{
    [HttpGet("/plans")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var result = await apiClient.GetPlansAsync(cancellationToken);
        return View(new PlanListViewModel
        {
            Data = result.Data,
            IsManager = IsManager,
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

    [HttpGet("/plans/new")]
    public IActionResult Create() => IsManager ? View("Form", new PlanFormViewModel()) : Denied();

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
