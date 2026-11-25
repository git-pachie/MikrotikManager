using Microsoft.AspNetCore.Mvc;
using Netora.Application.Common;
using Netora.Application.Customers;
using Netora.WebUI.Models;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

public class CustomersController(INetoraApiClient apiClient) : Controller
{
    public const string ToastKey = "Toast";

    [HttpGet("/customers")]
    public async Task<IActionResult> Index(string? search, string? status, string? nap, string? plan,
        int page = 1, int pageSize = 10, string? sortBy = null, string? sortDir = null,
        CancellationToken cancellationToken = default)
    {
        if (!CustomerListQuery.AllowedPageSizes.Contains(pageSize)) pageSize = 10;
        sortBy = CustomerListQuery.SortColumns.Contains(sortBy) ? sortBy : null;
        var descending = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase);

        var query = new CustomerListQuery(search, Normalize(status), Normalize(nap), Normalize(plan), Math.Max(1, page), pageSize,
            sortBy, descending ? "desc" : "asc");
        var result = await apiClient.GetCustomersAsync(query, cancellationToken);
        return View(new CustomerListViewModel
        {
            Search = search?.Trim(),
            Status = Normalize(status) ?? "All",
            Nap = Normalize(nap) ?? "All",
            Plan = Normalize(plan) ?? "All",
            PageSize = pageSize,
            SortBy = sortBy,
            Descending = sortBy is not null && descending,
            Data = result.Success ? result.Data : null,
            ErrorMessage = result.Success ? null : "Customers could not be loaded. The Web API is unavailable; try again in a moment.",
        });
    }

    [HttpGet("/customers/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var result = await apiClient.GetCustomerAsync(id, cancellationToken);
        if (result.Success && result.Data is not null)
        {
            // Checked on opening (and on Refresh, which reloads the page), never on a schedule (09.03).
            var router = await apiClient.GetRouterStatusAsync(id, cancellationToken);
            return View(new CustomerDetailViewModel { Data = result.Data, Router = router.Data });
        }

        if (result.Error?.Code == "not_found")
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return View("NotFound");
        }

        return View(new CustomerDetailViewModel
        {
            ErrorMessage = "The customer could not be loaded. The Web API is unavailable; try again in a moment.",
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

        AddApiErrors(result.Error, "The customer was not saved.");
        await FillOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpGet("/customers/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var result = await apiClient.GetCustomerAsync(id, cancellationToken);
        if (!result.Success || result.Data is null)
        {
            if (result.Error?.Code == "not_found")
            {
                Response.StatusCode = StatusCodes.Status404NotFound;
                return View("NotFound");
            }
            TempData[ToastKey] = "The customer could not be loaded. The Web API is unavailable; try again in a moment.";
            return Redirect("/customers");
        }

        if (!result.Data.IsEditable)
        {
            TempData[ToastKey] = "Terminated customers are view-only for Staff.";
            return Redirect($"/customers/{id}");
        }

        var model = CustomerFormViewModel.FromDetail(result.Data);
        await FillOptionsAsync(model, cancellationToken);
        return View(model);
    }

    [HttpPost("/customers/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CustomerFormViewModel model, CancellationToken cancellationToken)
    {
        model.Id = id;
        model.CanSetManagerOnly = IsManager;
        if (!ModelState.IsValid)
        {
            await RefillEditStateAsync(model, cancellationToken);
            return View(model);
        }

        var result = await apiClient.UpdateCustomerAsync(id, model.ToUpdateRequest(), cancellationToken);
        if (result.Success && result.Data is not null)
        {
            TempData[ToastKey] = result.Data.Changed ? "Customer updated." : "No changes to save.";
            return Redirect($"/customers/{id}");
        }

        switch (result.Error?.Code)
        {
            case "not_found":
                Response.StatusCode = StatusCodes.Status404NotFound;
                return View("NotFound");
            case "customer_locked":
                TempData[ToastKey] = "Terminated customers are view-only for Staff.";
                return Redirect($"/customers/{id}");
            case "concurrency_conflict":
                ModelState.AddModelError(string.Empty,
                    "This customer was changed by someone else while you were editing. Reload the page to see the latest values; your edits were not saved.");
                break;
            default:
                AddApiErrors(result.Error, "The customer was not saved.");
                break;
        }

        await RefillEditStateAsync(model, cancellationToken);
        return View(model);
    }

    /// <summary>
    /// Passes the upload to the Web API, which checks the type, size and permissions. The WebUI only sets a generous
    /// ceiling so that a request the API would accept is not cut off here; the API's configured limit is authoritative.
    /// </summary>
    [HttpPost("/customers/{id:int}/attachments")]
    [RequestSizeLimit(UploadCeilingBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadCeilingBytes)]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            TempData[ErrorKey] = "Choose a file to upload.";
            return Redirect(AttachmentsTab(id));
        }

        // Check the size first: a file far over the limit makes the API close the connection, which would otherwise
        // surface as a misleading "API unavailable" message. The API still enforces the limit itself.
        var customer = await apiClient.GetCustomerAsync(id, cancellationToken);
        if (customer.Error?.Code == "not_found") return CustomerNotFound();
        if (customer.Data is { } d && file.Length > d.MaxAttachmentBytes)
        {
            TempData[ErrorKey] = $"The file was not uploaded. It is larger than the {Netora.Application.Customers.Attachments.AttachmentFileTypes.FormatSize(d.MaxAttachmentBytes)} limit.";
            return Redirect(AttachmentsTab(id));
        }

        await using var stream = file.OpenReadStream();
        var result = await apiClient.UploadAttachmentAsync(id, stream, file.FileName, cancellationToken);
        if (result.Success)
            TempData[ToastKey] = "File uploaded.";
        else if (result.Error?.Code == "not_found")
            return CustomerNotFound();
        else
            TempData[ErrorKey] = result.Error?.Code == "api_unreachable"
                ? "The file was not uploaded. The Web API is unavailable; try again in a moment."
                : "The file was not uploaded. " + result.Error?.Message;

        return Redirect(AttachmentsTab(id));
    }

    [HttpGet("/customers/{id:int}/attachments/{attachmentId:long}")]
    public async Task<IActionResult> DownloadAttachment(int id, long attachmentId, CancellationToken cancellationToken)
    {
        var file = await apiClient.DownloadAttachmentAsync(id, attachmentId, cancellationToken);
        if (file.Response is null)
        {
            if (file.ErrorCode == "not_found") return CustomerNotFound();
            TempData[ErrorKey] = "The file could not be downloaded. Try again in a moment.";
            return Redirect(AttachmentsTab(id));
        }

        // Dispose the API response only after the body has been streamed to the browser.
        HttpContext.Response.RegisterForDispose(file.Response);
        var content = file.Response.Content;
        var name = content.Headers.ContentDisposition?.FileNameStar ?? content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "attachment";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        return File(await content.ReadAsStreamAsync(cancellationToken),
            content.Headers.ContentType?.MediaType ?? "application/octet-stream", name);
    }

    [HttpPost("/customers/{id:int}/attachments/{attachmentId:long}/delete")]
    public async Task<IActionResult> DeleteAttachment(int id, long attachmentId, CancellationToken cancellationToken)
    {
        var result = await apiClient.DeleteAttachmentAsync(id, attachmentId, cancellationToken);
        if (result.Success)
            TempData[ToastKey] = "File deleted.";
        else if (result.Error?.Code == "not_found")
            return CustomerNotFound();
        else
            TempData[ErrorKey] = "The file was not deleted. " + (result.Error?.Message ?? "Unexpected error.");
        return Redirect(AttachmentsTab(id));
    }

    public const string ErrorKey = "ToastError";

    /// <summary>WebUI pass-through ceiling. Must be at least the API's Attachments:MaxFileSizeBytes.</summary>
    private const long UploadCeilingBytes = 100L * 1024 * 1024;

    private static string AttachmentsTab(int id) => $"/customers/{id}#attachments";

    private ViewResult CustomerNotFound()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("NotFound");
    }

    private bool IsManager => User.IsInRole("Manager");

    /// <summary>
    /// After a failed save the posted form only has the typed fields. Reload the read-only context (status label,
    /// locked flag, current stamp) from the API, so the page renders the same as the first time.
    /// </summary>
    private async Task RefillEditStateAsync(CustomerFormViewModel model, CancellationToken cancellationToken)
    {
        var current = await apiClient.GetCustomerAsync(model.Id!.Value, cancellationToken);
        if (current.Success && current.Data is not null)
        {
            model.DisplayId = current.Data.DisplayId;
            model.StatusLabel = CustomerListViewModel.StatusLabel(current.Data.Status);
            model.ServiceFieldsLocked = current.Data.ServiceFieldsLocked;
        }
        await FillOptionsAsync(model, cancellationToken);
    }

    /// <summary>Maps API field errors onto the form; anything else becomes a form-level message.</summary>
    private void AddApiErrors(ApiError? error, string failurePrefix)
    {
        if (error?.Details is { Count: > 0 } details)
        {
            foreach (var (field, messages) in details)
            {
                var key = typeof(CustomerFormViewModel).GetProperty(field) is not null ? field : string.Empty;
                foreach (var message in messages) ModelState.AddModelError(key, message);
            }
            return;
        }

        ModelState.AddModelError(string.Empty, error?.Code switch
        {
            "forbidden" => error.Message,
            "api_unreachable" => $"{failurePrefix} The Web API is unavailable; try again in a moment.",
            _ => $"{failurePrefix} {error?.Message ?? "Unexpected error."}",
        });
    }

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
