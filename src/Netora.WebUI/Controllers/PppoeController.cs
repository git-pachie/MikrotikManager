using Microsoft.AspNetCore.Mvc;
using Netora.Application.Common;
using Netora.Application.Pppoe;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

public class PppoeController(INetoraApiClient apiClient) : Controller
{
    [HttpGet("/pppoe")]
    public async Task<IActionResult> Index(string? search, string? state, int page = 1, int pageSize = 10,
        string? sortBy = null, string? sortDir = null, CancellationToken cancellationToken = default)
    {
        if (!PppoeListQuery.AllowedPageSizes.Contains(pageSize)) pageSize = 10;
        state = PppoeListQuery.States.Contains(state) ? state : null;
        sortBy = PppoeListQuery.SortColumns.Contains(sortBy) ? sortBy : null;
        var descending = sortBy is not null && string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        var result = await apiClient.GetPppoeAccountsAsync(
            new PppoeListQuery(search?.Trim(), state, Math.Max(1, page), pageSize, sortBy, descending ? "desc" : "asc"),
            cancellationToken);
        return View(new PppoeListViewModel
        {
            Search = search?.Trim(),
            State = state ?? "all",
            PageSize = pageSize,
            SortBy = sortBy,
            Descending = descending,
            Data = result.Success ? result.Data : null,
            ErrorMessage = result.Success ? null : "PPPoE accounts could not be loaded. The Web API is unavailable; try again in a moment.",
        });
    }

    [HttpGet("/pppoe/import")]
    public IActionResult Import() => View();

    [HttpGet("/pppoe/{id:int}")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var result = await apiClient.GetPppoeAccountAsync(id, cancellationToken);
        if (result.Data is null) return LoadFailed(result.Error);
        var model = PppoeFormViewModel.From(result.Data);
        await FillAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost("/pppoe/{id:int}")]
    public async Task<IActionResult> Edit(int id, PppoeFormViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await apiClient.UpdatePppoeAccountAsync(id, model.ToRequest(), cancellationToken);
            if (result.Success)
            {
                TempData[CustomersController.ToastKey] = result.Data?.Changed == true ? "PPPoE details saved. Not sent to the router yet." : "No changes to save.";
                return Redirect($"/pppoe/{id}");
            }
            if (result.Error?.Code == "not_found") return NotFoundPage();
            AddErrors(result.Error);
        }

        // Re-render with the current account state; keep what the user typed (except the password).
        var current = await apiClient.GetPppoeAccountAsync(id, cancellationToken);
        if (current.Data is null) return LoadFailed(current.Error);
        if (ModelState[string.Empty]?.Errors.Any(e => e.ErrorMessage.Contains("changed by someone else")) == true)
        {
            // Stale form: show the latest saved values with a fresh stamp instead of what was typed.
            var error = ModelState[string.Empty]!.Errors[0].ErrorMessage;
            ModelState.Clear();
            model = PppoeFormViewModel.From(current.Data);
            ModelState.AddModelError(string.Empty, error);
        }
        model.Detail = current.Data;
        model.NewPassword = null;
        ModelState.Remove(nameof(PppoeFormViewModel.NewPassword));
        await FillAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost("/pppoe/{id:int}/requests")]
    public async Task<IActionResult> Submit(int id, string operation, string? reason, string? targetPlan, CancellationToken cancellationToken)
    {
        var result = await apiClient.SubmitPppoeRequestAsync(id, new SubmitPppoeRequest(operation, reason, targetPlan), cancellationToken);
        if (result.Error?.Code == "not_found") return NotFoundPage();
        if (result.Success && result.Data is { } r)
        {
            var requestId = Netora.Domain.Approvals.ApprovalRequest.DisplayId(r.Id);
            TempData[CustomersController.ToastKey] = r.Status == "Approved"
                ? r.Operation switch
                {
                    "ChangePlan" => $"Plan changed ({requestId}, approved by you). Not sent to the router yet.",
                    "TerminatePppoe" => $"Account terminated ({requestId}, approved by you). Not sent to the router yet.",
                    _ => $"Account {PppoeLabels.Operation(r.Operation).ToLowerInvariant()}d ({requestId}, approved by you). Not sent to the router yet.",
                }
                : $"{requestId} sent: {Netora.WebUI.Models.ApprovalLabels.Operation(r.Operation).ToLowerInvariant()}. A Manager must approve it.";
        }
        else
        {
            TempData[CustomersController.ErrorKey] = "The request was not sent. " + Message(result.Error);
        }
        return Redirect($"/pppoe/{id}");
    }

    [HttpPost("/pppoe/{id:int}/requests/{requestId:long}/decision")]
    public async Task<IActionResult> Decide(int id, long requestId, bool approve, string? note, CancellationToken cancellationToken)
    {
        var result = await apiClient.DecidePppoeRequestAsync(id, requestId, new DecidePppoeRequest(approve, note), cancellationToken);
        if (result.Error?.Code == "not_found") return NotFoundPage();
        if (result.Success)
            TempData[CustomersController.ToastKey] = approve ? "Request approved. Not sent to the router yet." : "Request rejected.";
        else
            TempData[CustomersController.ErrorKey] = "The decision was not saved. " + Message(result.Error);
        return Redirect($"/pppoe/{id}");
    }

    private async Task FillAsync(PppoeFormViewModel model, CancellationToken cancellationToken)
    {
        model.IsManager = User.IsInRole("Manager");
        var options = await apiClient.GetCustomerFormOptionsAsync(cancellationToken);
        var plans = options.Data?.Plans.ToList() ?? [];
        if (!plans.Contains(model.PlanName) && !string.IsNullOrEmpty(model.PlanName)) plans.Insert(0, model.PlanName);
        model.PlanOptions = plans;
    }

    private void AddErrors(ApiError? error)
    {
        if (error?.Details is { Count: > 0 } details)
        {
            foreach (var (field, messages) in details)
            {
                var key = typeof(PppoeFormViewModel).GetProperty(field) is not null ? field : string.Empty;
                foreach (var message in messages) ModelState.AddModelError(key, message);
            }
            return;
        }
        ModelState.AddModelError(string.Empty, error?.Code == "concurrency_conflict"
            ? "This account was changed by someone else while you were editing. Your changes were not saved; the latest values are shown below."
            : "The changes were not saved. " + Message(error));
    }

    private static string Message(ApiError? error) => error?.Code == "api_unreachable"
        ? "The Web API is unavailable; try again in a moment."
        : error?.Message ?? "Unexpected error.";

    private IActionResult LoadFailed(ApiError? error)
    {
        if (error?.Code == "not_found") return NotFoundPage();
        TempData[CustomersController.ErrorKey] = "The PPPoE account could not be loaded. " + Message(error);
        return Redirect("/pppoe");
    }

    private ViewResult NotFoundPage()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("NotFound");
    }
}
