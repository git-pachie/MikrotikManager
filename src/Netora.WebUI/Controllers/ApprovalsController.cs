using Microsoft.AspNetCore.Mvc;
using Netora.Application.Approvals;
using Netora.Application.Common;
using Netora.Application.Pppoe;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

/// <summary>Pending Approvals list and Approval Details (08.02). The API enforces who may see and decide.</summary>
public class ApprovalsController(INetoraApiClient apiClient) : Controller
{
    [HttpGet("/approvals")]
    public async Task<IActionResult> Index(string? status, string? search, int page = 1, int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        status = ApprovalListQuery.Statuses.Contains(status) ? status! : "pending";
        if (!ApprovalListQuery.AllowedPageSizes.Contains(pageSize)) pageSize = 10;
        search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        var result = await apiClient.GetApprovalsAsync(new ApprovalListQuery(status, null, search, Math.Max(1, page), pageSize), cancellationToken);
        return View(new ApprovalListViewModel
        {
            Status = status,
            Search = search,
            PageSize = pageSize,
            IsManager = User.IsInRole("Manager"),
            Data = result.Success ? result.Data : null,
            ErrorMessage = result.Success ? null : "Approval requests could not be loaded. The Web API is unavailable; try again in a moment.",
        });
    }

    [HttpGet("/approvals/{id:long}")]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var result = await apiClient.GetApprovalAsync(id, cancellationToken);
        if (result.Data is null)
        {
            if (result.Error?.Code == "not_found") return NotFoundPage();
            TempData[CustomersController.ErrorKey] = "The request could not be loaded. " + Message(result.Error);
            return Redirect("/approvals");
        }
        return View(new ApprovalDetailViewModel { Detail = result.Data, IsManager = User.IsInRole("Manager") });
    }

    [HttpPost("/approvals/{id:long}/decision")]
    public async Task<IActionResult> Decide(long id, bool approve, string? note, CancellationToken cancellationToken)
    {
        var result = await apiClient.DecideApprovalAsync(id, new DecidePppoeRequest(approve, note), cancellationToken);
        if (result.Error?.Code == "not_found") return NotFoundPage();
        if (result.Success && result.Data is { } r)
        {
            TempData[CustomersController.ToastKey] = r.Status == "Approved"
                ? $"{ApprovalRequestId(r.Id)} approved. The change is saved; it is not sent to the router yet."
                : $"{ApprovalRequestId(r.Id)} rejected. Nothing was changed.";
        }
        else
        {
            TempData[CustomersController.ErrorKey] = "The decision was not saved. " + Message(result.Error);
        }
        return Redirect($"/approvals/{id}");
    }

    private static string ApprovalRequestId(long id) => Domain.Approvals.ApprovalRequest.DisplayId(id);

    private static string Message(ApiError? error) => error?.Code switch
    {
        "api_unreachable" => "The Web API is unavailable; try again in a moment.",
        "forbidden" => "Only Managers can approve or reject requests.",
        _ => error?.Message ?? "Unexpected error.",
    };

    private ViewResult NotFoundPage()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("NotFound");
    }
}
