using Microsoft.AspNetCore.Mvc;
using Netora.Application.Customers;
using Netora.Application.Operations;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

public sealed class OnuController(INetoraApiClient api) : Controller
{
    [HttpGet("/onu-history")]
    public async Task<IActionResult> Index(int? customerId, string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        var response = await api.GetOnuHistoryAsync(customerId, search, page, cancellationToken);
        return response.Error?.Code == "not_found" ? CustomerNotFound()
            : View(new OnuHistoryViewModel(response.Data, customerId, search, response.Error?.Message));
    }

    [HttpGet("/onu-history/replace")]
    public async Task<IActionResult> Select(string? search, int page = 1, CancellationToken cancellationToken = default)
    {
        var result = await api.GetCustomersAsync(new CustomerListQuery(Search: search, Page: Math.Clamp(page, 1, int.MaxValue / 20),
            PageSize: 20, SortBy: "name"), cancellationToken);
        ViewData["Search"] = search;
        ViewData["Error"] = result.Error?.Message;
        return View(result.Data);
    }

    [HttpGet("/onu-history/{id:int}/replace")]
    public async Task<IActionResult> Replace(int id, CancellationToken cancellationToken)
    {
        var result = await api.GetCustomerAsync(id, cancellationToken);
        if (result.Error?.Code == "not_found") return CustomerNotFound();
        if (result.Data is null)
        {
            TempData[CustomersController.ErrorKey] = result.Error?.Message ?? "The customer could not be loaded.";
            return Redirect("/onu-history");
        }
        return View(new ReplaceOnuViewModel { Customer = result.Data, ConcurrencyStamp = result.Data.ConcurrencyStamp });
    }

    [HttpPost("/onu-history/{id:int}/replace")]
    public async Task<IActionResult> Replace(int id, ReplaceOnuViewModel model, CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var result = await api.ReplaceOnuAsync(id,
                new ReplaceOnuRequest(model.Serial, model.DeviceModel, model.ReplacedOn, model.Reason, model.ConcurrencyStamp), cancellationToken);
            if (result.Success)
            {
                TempData[CustomersController.ToastKey] = "ONU replacement recorded.";
                return Redirect($"/onu-history?customerId={id}");
            }
            if (result.Error?.Code == "not_found") return CustomerNotFound();
            if (result.Error?.Code == "concurrency_conflict")
            {
                TempData[CustomersController.ErrorKey] = result.Error.Message;
                return Redirect($"/onu-history/{id}/replace");
            }
            ApiErrors.Apply(ModelState, result.Error, typeof(ReplaceOnuViewModel), "The replacement could not be recorded.");
        }

        var current = await api.GetCustomerAsync(id, cancellationToken);
        if (current.Error?.Code == "not_found") return CustomerNotFound();
        model.Customer = current.Data;
        return View(model);
    }

    private ViewResult CustomerNotFound()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        ViewData["NotFoundTitle"] = "Customer not found";
        return View("NotFound");
    }
}
