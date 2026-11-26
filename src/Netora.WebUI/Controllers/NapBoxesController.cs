using Microsoft.AspNetCore.Mvc;
using Netora.Application.Catalog;
using Netora.Application.Customers;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

/// <summary>
/// NAP Boxes (06, nap-boxes.html and nap-details.html). Everyone sees the list and details; Managers add and edit;
/// Staff and Managers move customers between NAP boxes. The API enforces the same rules.
/// </summary>
public class NapBoxesController(INetoraApiClient apiClient) : Controller
{
    [HttpGet("/nap-boxes")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var result = await apiClient.GetNapBoxesAsync(cancellationToken);
        ViewData["Error"] = result.Success ? null : "NAP boxes could not be loaded. The Web API is unavailable; try again in a moment.";
        ViewData["IsManager"] = IsManager;
        return View(result.Data);
    }

    [HttpGet("/nap-boxes/new")]
    public IActionResult Create() => IsManager ? View(new NapFormViewModel()) : Denied();

    [HttpPost("/nap-boxes/new")]
    public async Task<IActionResult> Create(NapFormViewModel model, CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        if (!ModelState.IsValid) return View(model);

        var result = await apiClient.CreateNapBoxAsync(model.ToRequest(), cancellationToken);
        if (result.Data is { } saved)
        {
            TempData[CustomersController.ToastKey] = $"NAP box {model.Code.Trim()} added.";
            return Redirect($"/nap-boxes/{saved.Id}");
        }
        ApiErrors.Apply(ModelState, result.Error, typeof(NapFormViewModel), "The NAP box was not saved.");
        return View(model);
    }

    [HttpGet("/nap-boxes/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var result = await apiClient.GetNapBoxAsync(id, cancellationToken);
        if (result.Data is { } nap)
        {
            return View(new NapDetailViewModel
            {
                Data = nap,
                IsManager = IsManager,
                Form = new NapFormViewModel
                {
                    Code = nap.Code, Area = nap.Area, Description = nap.Description, IsActive = nap.IsActive, ConcurrencyStamp = nap.ConcurrencyStamp,
                },
            });
        }
        if (result.Error?.Code == "not_found") return NotFoundPage();
        TempData[CustomersController.ErrorKey] = "The NAP box could not be loaded. The Web API is unavailable; try again in a moment.";
        return Redirect("/nap-boxes");
    }

    [HttpPost("/nap-boxes/{id:int}")]
    public async Task<IActionResult> Details(int id, NapFormViewModel model, CancellationToken cancellationToken)
    {
        if (!IsManager) return Denied();
        if (ModelState.IsValid)
        {
            var result = await apiClient.UpdateNapBoxAsync(id, model.ToRequest(), cancellationToken);
            if (result.Success)
            {
                TempData[CustomersController.ToastKey] = result.Data!.Changed ? "NAP box saved." : "No changes to save.";
                return Redirect($"/nap-boxes/{id}");
            }
            switch (result.Error?.Code)
            {
                case "not_found":
                    return NotFoundPage();
                case "concurrency_conflict":
                    ModelState.AddModelError(string.Empty,
                        "This NAP box was changed by someone else while you were editing. Reload the page to see the latest values; your edits were not saved.");
                    break;
                default:
                    ApiErrors.Apply(ModelState, result.Error, typeof(NapFormViewModel), "The NAP box was not saved.");
                    break;
            }
        }

        var current = await apiClient.GetNapBoxAsync(id, cancellationToken);
        if (current.Data is not { } nap)
        {
            if (current.Error?.Code == "not_found") return NotFoundPage();
            TempData[CustomersController.ErrorKey] = "The NAP box was not saved. The Web API is unavailable; try again in a moment.";
            return Redirect("/nap-boxes");
        }
        return View(new NapDetailViewModel { Data = nap, IsManager = true, Form = model });
    }

    /// <summary>Moves one customer to another NAP box (no approval, no router call). Back to the NAP it came from.</summary>
    [HttpPost("/nap-boxes/{id:int}/customers/{customerId:int}/move")]
    public async Task<IActionResult> Move(int id, int customerId, string? napBox, string? concurrencyStamp, CancellationToken cancellationToken)
    {
        var result = await apiClient.MoveCustomerNapAsync(customerId, new MoveNapRequest(napBox ?? string.Empty, concurrencyStamp ?? string.Empty), cancellationToken);
        if (result.Success)
            TempData[CustomersController.ToastKey] = result.Data!.Changed ? $"Customer NET-{customerId:0000} moved to {napBox}." : "The customer is already on that NAP box.";
        else
            TempData[CustomersController.ErrorKey] = "The customer was not moved. " + result.Error?.Code switch
            {
                "concurrency_conflict" => "The customer was changed by someone else meanwhile; check the list and try again.",
                "customer_locked" => "Terminated customers are view-only for Staff.",
                "not_found" => "The customer no longer exists or is not visible to you.",
                "api_unreachable" => "The Web API is unavailable; try again in a moment.",
                _ => string.Join(" ", result.Error?.Details?.SelectMany(d => d.Value) ?? [result.Error?.Message ?? "Unexpected error."]),
            };
        return Redirect($"/nap-boxes/{id}");
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
        ViewData["NotFoundTitle"] = "NAP box not found";
        return View("NotFound");
    }
}
