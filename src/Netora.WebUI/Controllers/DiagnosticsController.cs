using Microsoft.AspNetCore.Mvc;
using Netora.Application.Customers;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

public sealed class DiagnosticsController(INetoraApiClient api) : Controller
{
    [HttpGet("/diagnostics")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(int? customerId, string? search, string? check, int page = 1,
        CancellationToken cancellationToken = default)
    {
        page = Math.Clamp(page, 1, int.MaxValue / 20);
        var list = await api.GetCustomersAsync(new CustomerListQuery(Search: search, Page: page, PageSize: 20, SortBy: "name"),
            cancellationToken);
        var checkType = check == "mismatch" ? "mismatch" : "session";
        if (customerId is not { } id)
            return View(new DiagnosticViewModel(list.Data, null, null, list.Error?.Message, search, checkType, page));

        var customer = await api.GetCustomerAsync(id, cancellationToken);
        if (customer.Error?.Code == "not_found")
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            ViewData["NotFoundTitle"] = "Customer not found";
            return View("NotFound");
        }
        if (customer.Data is null || check is null)
            return View(new DiagnosticViewModel(list.Data, customer.Data, null, customer.Error?.Message ?? list.Error?.Message,
                search, checkType, page));

        var result = await api.GetDiagnosticsAsync(id, checkType == "session", cancellationToken);
        return View(new DiagnosticViewModel(list.Data, customer.Data, result.Data, result.Error?.Message, search, checkType, page));
    }
}
