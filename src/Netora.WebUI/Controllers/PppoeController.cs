using Microsoft.AspNetCore.Mvc;
using Netora.Application.Common;
using Netora.Application.Pppoe;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

public class PppoeController(INetoraApiClient apiClient) : Controller
{
    [HttpPost("/pppoe/{id:int}/password")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> RevealPassword(int id, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Manager")) return Forbid();
        var result = await apiClient.RevealPppoePasswordAsync(id, cancellationToken);
        return result.Data is { } password
            ? Json(password)
            : BadRequest(new { message = result.Error?.Message ?? "The saved password could not be loaded." });
    }

    [HttpGet("/pppoe/{id:int}/delete")]
    public async Task<IActionResult> ConfirmDelete(int id, string? from, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Manager")) return DeleteDenied();
        var result = await apiClient.GetPppoeAccountAsync(id, cancellationToken);
        if (result.Data is not { } account) return LoadFailed(result.Error);
        if (account.DeletionRequestId is { } existing) return Redirect($"/approvals/{existing}");
        if (account.CanDelete)
        {
            ViewData["DeleteSource"] = from;
            ViewData["DeleteReturnPath"] = DeleteReturnPath(id, from);
            return View("Delete", account);
        }

        TempData[CustomersController.ErrorKey] = "This PPPoE account cannot be deleted. Reload and check its current state.";
        return Redirect($"/pppoe/{id}");
    }

    [HttpPost("/pppoe/{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, string? concurrencyStamp, string? from, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Manager")) return DeleteDenied();
        var result = await apiClient.DeletePppoeAccountAsync(id, new DeletePppoeRequest(concurrencyStamp ?? string.Empty), cancellationToken);
        if (result.Success && result.Data is { } deletion)
        {
            if (deletion.ProvisioningStatus == "Succeeded")
            {
                TempData[CustomersController.ToastKey] = "PPPoE account deleted.";
                return Redirect(DeleteReturnPath(id, from, completed: true));
            }

            TempData[CustomersController.ErrorKey] = "Deletion was authorized. Review the router result and retry from this request if needed.";
            return Redirect($"/approvals/{deletion.Id}");
        }
        if (result.Error?.Code == "not_found") return NotFoundPage();
        TempData[CustomersController.ErrorKey] = Message(result.Error);
        return Redirect(DeleteReturnPath(id, from));
    }

    private static string DeleteReturnPath(int id, string? from, bool completed = false) => from switch
    {
        "customers" => "/customers",
        "customer" => $"/customers/{id}",
        _ => completed ? "/pppoe" : $"/pppoe/{id}",
    };

    private ViewResult DeleteDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View("~/Views/Account/AccessDenied.cshtml");
    }

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
    public async Task<IActionResult> Import(CancellationToken cancellationToken)
        => View(await LoadImportAsync(cancellationToken));

    /// <summary>On-demand scan (04.04): reads the router's secrets and shows them for review. Imports nothing.</summary>
    [HttpPost("/pppoe/import/scan")]
    public async Task<IActionResult> Scan(CancellationToken cancellationToken)
    {
        var model = await LoadImportAsync(cancellationToken);
        if (!model.IsManager) return View("Import", model with { Error = "Only Managers can scan the router and import accounts." });
        var result = await apiClient.ScanImportAsync(cancellationToken);
        return View("Import", result.Success
            ? model with { Scan = result.Data }
            : model with { Error = "The router could not be scanned. " + (result.Error?.Message ?? "Unexpected error.") });
    }

    private async Task<PppoeImportViewModel> LoadImportAsync(CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Manager")) return new PppoeImportViewModel();
        var settings = await apiClient.GetSettingsAsync(cancellationToken);
        return new PppoeImportViewModel { IsManager = true, Router = settings.Data?.Router };
    }

    /// <summary>Imports only the secrets the Manager ticked after a scan.</summary>
    [HttpPost("/pppoe/import")]
    public async Task<IActionResult> ConfirmImport(List<string>? names, string? previewMode, CancellationToken cancellationToken)
    {
        if (!User.IsInRole("Manager")) return View("Import", new PppoeImportViewModel { Error = "Only Managers can import accounts." });
        var result = await apiClient.ImportPppoeAsync(new Netora.Application.Router.ImportRequest(names ?? [], previewMode), cancellationToken);
        if (result.Success && result.Data is { } r)
        {
            TempData[CustomersController.ToastKey] = $"{r.Imported} account(s) imported with NAP UNKNOWN, {r.Skipped} skipped." +
                (r.Messages.Count > 0 ? " " + string.Join(" ", r.Messages.Take(10)) : string.Empty);
            if (r.Imported == 0) return Redirect("/pppoe/import");
            return Redirect(names is [var username]
                ? Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("/customers?nap=UNKNOWN", "search", username)
                : "/customers?nap=UNKNOWN");
        }
        TempData[CustomersController.ErrorKey] = "Nothing was imported. " + (result.Error?.Message ?? "Unexpected error.");
        return Redirect("/pppoe/import");
    }

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
        if (ModelState.ContainsKey(nameof(PppoeFormViewModel.NewPassword)))
            ModelState.SetModelValue(nameof(PppoeFormViewModel.NewPassword), rawValue: null, attemptedValue: null);
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
                    "DisablePppoe" or "EnablePppoe" => r.ProvisioningStatus switch
                    {
                        "Succeeded" => $"{PppoeLabels.Operation(r.Operation)} completed on the router ({requestId}).",
                        "Failed" => $"{requestId} approved. The router update failed. Open Request History to retry.",
                        _ => $"{requestId} approved. Open Request History to check the router status.",
                    },
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
