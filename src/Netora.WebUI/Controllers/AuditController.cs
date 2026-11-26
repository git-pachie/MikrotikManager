using Microsoft.AspNetCore.Mvc;
using Netora.Application.Audit;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

/// <summary>Audit Trail (12.01, audit.html). Managers only; the API enforces the same.</summary>
public class AuditController(INetoraApiClient apiClient) : Controller
{
    [HttpGet("/audit")]
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, string? category, string? actor, string? search,
        int page = 1, int pageSize = 25, CancellationToken cancellationToken = default)
    {
        if (!User.IsInRole("Manager"))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return View("~/Views/Account/AccessDenied.cshtml");
        }

        var model = new AuditViewModel
        {
            From = from,
            To = to,
            Category = AuditCategories.All.FirstOrDefault(c => c == category),
            Actor = string.IsNullOrWhiteSpace(actor) ? null : actor.Trim(),
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            PageSize = AuditQuery.AllowedPageSizes.Contains(pageSize) ? pageSize : 25,
        };
        if (from > to)
        {
            model.ErrorMessage = "The start date must be on or before the end date.";
            return View(model);
        }

        var result = await apiClient.GetAuditAsync(
            new AuditQuery(model.From, model.To, model.Category, model.Actor, model.Search, Math.Max(1, page), model.PageSize), cancellationToken);
        model.Data = result.Data;
        model.ErrorMessage = result.Success ? null : "The audit trail could not be loaded. " + (result.Error?.Code == "api_unreachable"
            ? "The Web API is unavailable; try again in a moment."
            : result.Error?.Message);
        return View(model);
    }
}
