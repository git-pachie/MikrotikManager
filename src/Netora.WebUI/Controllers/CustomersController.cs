using Microsoft.AspNetCore.Mvc;
using Netora.Application.Customers;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

public class CustomersController(INetoraApiClient apiClient) : Controller
{
    public const string ToastKey = "Toast";

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

    [HttpGet("/customers/new")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var model = new CustomerFormViewModel();
        await FillOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost("/customers/new")]
    public async Task<IActionResult> Create(CustomerFormViewModel model, CancellationToken cancellationToken)
    {
        model.CanSetManagerOnly = IsManager;
        if (model.IsManagerOnly && !model.CanSetManagerOnly)
            ModelState.AddModelError(string.Empty, "Only Managers can restrict customer visibility.");

        if (!ModelState.IsValid)
        {
            await FillOptionsAsync(model, cancellationToken);
            return View(model);
        }

        var result = await apiClient.CreateCustomerAsync(model.ToRequest(), cancellationToken);
        if (result.Success && result.Data is not null)
        {
            TempData[ToastKey] = $"Customer saved as Pending Approval (ID {result.Data.Id}). A Manager must approve it before the PPPoE account is created on the router.";
            return Redirect("/customers");
        }

        // Map API field errors onto the form; anything else becomes a form-level message.
        if (result.Error?.Details is { Count: > 0 } details)
        {
            foreach (var (field, messages) in details)
            {
                var key = typeof(CustomerFormViewModel).GetProperty(field) is not null ? field : string.Empty;
                foreach (var message in messages) ModelState.AddModelError(key, message);
            }
        }
        else
        {
            ModelState.AddModelError(string.Empty, result.Error?.Code switch
            {
                "forbidden" => result.Error.Message,
                "api_unreachable" => "The customer was not saved. The Web API is unavailable; try again in a moment.",
                _ => "The customer was not saved. " + (result.Error?.Message ?? "Unexpected error."),
            });
        }

        await FillOptionsAsync(model, cancellationToken);
        return View(model);
    }

    private bool IsManager => User.IsInRole("Manager");

    private async Task FillOptionsAsync(CustomerFormViewModel model, CancellationToken cancellationToken)
    {
        model.CanSetManagerOnly = IsManager;
        var options = await apiClient.GetCustomerFormOptionsAsync(cancellationToken);
        if (options.Success && options.Data is not null)
        {
            model.PlanOptions = options.Data.Plans;
            model.NapOptions = options.Data.NapBoxes;
        }
        else
        {
            model.OptionsError = "Plan and NAP lists could not be loaded. Try again in a moment.";
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) || value.Equals("All", StringComparison.OrdinalIgnoreCase) ? null : value.Trim();
}
