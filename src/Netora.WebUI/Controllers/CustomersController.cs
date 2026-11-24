using Microsoft.AspNetCore.Mvc;
using Netora.Application.Customers;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

public class CustomersController(INetoraApiClient apiClient) : Controller
{
    [HttpGet("/customers")]
    public async Task<IActionResult> Index(string? search, string? status, string? nap, string? plan,
        int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        if (!CustomerListQuery.AllowedPageSizes.Contains(pageSize)) pageSize = 10;
        var query = new CustomerListQuery(search, Normalize(status), Normalize(nap), Normalize(plan), Math.Max(1, page), pageSize);

        var result = await apiClient.GetCustomersAsync(query, cancellationToken);

        return View(new CustomerListViewModel
        {
            Search = search?.Trim(),
            Status = Normalize(status) ?? "All",
            Nap = Normalize(nap) ?? "All",
            Plan = Normalize(plan) ?? "All",
            PageSize = pageSize,
            Data = result.Success ? result.Data : null,
            ErrorMessage = result.Success ? null : "Customers could not be loaded. The Web API is unavailable; try again in a moment.",
        });
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Equals("All", StringComparison.OrdinalIgnoreCase) ? null : value.Trim();
}
