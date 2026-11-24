using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Net.Http.Headers;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Customers;
using Netora.Application.Customers.Attachments;
using Netora.Infrastructure.Identity;

namespace Netora.WebApi.Controllers;

/// <summary>
/// Customer attachments (03.05). Every call first checks that the caller may see the customer (Manager-only rule), so
/// a hidden customer's files behave exactly like missing ones.
/// </summary>
[ApiController]
[Route("api/v1/customers/{customerId:int}/attachments")]
public sealed class CustomerAttachmentsController(ICustomerAttachmentService attachments) : ControllerBase
{
    /// <summary>Upload one JPG, PNG or PDF as multipart/form-data field "file". The size limit comes from settings.</summary>
    [HttpPost]
    [AttachmentUploadLimits]
    [ProducesResponseType(typeof(ApiResponse<AttachmentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<AttachmentDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<AttachmentDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<AttachmentDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<AttachmentDto>), StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Upload(int customerId, IFormFile? file, CancellationToken cancellationToken)
    {
        var traceId = HttpContext.TraceIdentifier;
        var actor = CurrentActor();
        if (actor is null)
            return Unauthorized(ApiResponse<AttachmentDto>.Fail("unauthorized", "Authentication is required.", traceId));
        if (file is null)
            return BadRequest(ApiResponse<AttachmentDto>.Fail("validation_failed", "Choose a file to upload.", traceId,
                new Dictionary<string, string[]> { ["file"] = ["Choose a file to upload."] }));

        await using var stream = file.OpenReadStream();
        var result = await attachments.UploadAsync(customerId, file.FileName, file.Length, stream, actor, cancellationToken);
        var maxText = AttachmentFileTypes.FormatSize(attachments.MaxFileSizeBytes);

        return result.Outcome switch
        {
            AttachmentOutcome.Ok => StatusCode(StatusCodes.Status201Created, ApiResponse<AttachmentDto>.Ok(result.Attachment!, traceId)),
            AttachmentOutcome.NotFound => NotFound(ApiResponse<AttachmentDto>.Fail("not_found", "Customer not found.", traceId)),
            AttachmentOutcome.CustomerLocked => Conflict(ApiResponse<AttachmentDto>.Fail("customer_locked",
                "Terminated customers are view-only for Staff.", traceId)),
            AttachmentOutcome.EmptyFile => BadRequest(ApiResponse<AttachmentDto>.Fail("empty_file", "The file is empty.", traceId)),
            AttachmentOutcome.TooLarge => StatusCode(StatusCodes.Status413PayloadTooLarge, ApiResponse<AttachmentDto>.Fail(
                "file_too_large", $"The file is larger than the {maxText} limit.", traceId)),
            AttachmentOutcome.UnsupportedType => BadRequest(ApiResponse<AttachmentDto>.Fail("unsupported_file_type",
                $"Only {AttachmentFileTypes.Labels} files are accepted.", traceId)),
            _ => throw new InvalidOperationException($"Unhandled outcome {result.Outcome}"),
        };
    }

    /// <summary>Downloads an attachment. Always sent as a download (Content-Disposition: attachment), never rendered inline.</summary>
    [HttpGet("{attachmentId:long}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(int customerId, long attachmentId, CancellationToken cancellationToken)
    {
        var traceId = HttpContext.TraceIdentifier;
        var result = await attachments.OpenAsync(customerId, attachmentId, IsManager, cancellationToken);
        switch (result.Outcome)
        {
            case AttachmentOutcome.Ok:
                Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
                Response.Headers[HeaderNames.CacheControl] = "private, no-store";
                return File(result.Content!, result.ContentType!, result.FileName);
            case AttachmentOutcome.FileMissing:
                return NotFound(ApiResponse<object>.Fail("file_missing",
                    "The file record exists but the file is missing from storage. Contact an administrator.", traceId));
            default:
                return NotFound(ApiResponse<object>.Fail("not_found", "Attachment not found.", traceId));
        }
    }

    /// <summary>Hides an attachment. The stored file and the record are kept for history (03.04).</summary>
    [HttpDelete("{attachmentId:long}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int customerId, long attachmentId, CancellationToken cancellationToken)
    {
        var traceId = HttpContext.TraceIdentifier;
        var actor = CurrentActor();
        if (actor is null)
            return Unauthorized(ApiResponse<object>.Fail("unauthorized", "Authentication is required.", traceId));

        return await attachments.DeleteAsync(customerId, attachmentId, actor, cancellationToken) switch
        {
            AttachmentOutcome.Ok => Ok(ApiResponse<object>.Ok(new { deleted = true }, traceId)),
            AttachmentOutcome.CustomerLocked => Conflict(ApiResponse<object>.Fail("customer_locked",
                "Terminated customers are view-only for Staff.", traceId)),
            _ => NotFound(ApiResponse<object>.Fail("not_found", "Attachment not found.", traceId)),
        };
    }

    private bool IsManager => User.IsInRole(Roles.Manager);

    private CustomerActor? CurrentActor()
    {
        var userId = User.FindFirst(NetoraClaimTypes.Subject)?.Value;
        if (string.IsNullOrEmpty(userId)) return null;
        return new CustomerActor(userId, User.FindFirst(NetoraClaimTypes.Name)?.Value ?? userId, IsManager);
    }
}

/// <summary>
/// Sets the request and multipart size limits for the upload endpoint from Attachments:MaxFileSizeBytes, plus room
/// for the multipart framing. Runs before model binding, which is when the limits are applied.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AttachmentUploadLimitsAttribute : Attribute, IResourceFilter
{
    private const long MultipartOverheadBytes = 1024 * 1024;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var max = context.HttpContext.RequestServices.GetRequiredService<ICustomerAttachmentService>().MaxFileSizeBytes
                  + MultipartOverheadBytes;

        var bodySize = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false }) bodySize.MaxRequestBodySize = max;

        context.HttpContext.Features.Set<IFormFeature>(new FormFeature(context.HttpContext.Request,
            new FormOptions { MultipartBodyLengthLimit = max }));
    }

    public void OnResourceExecuted(ResourceExecutedContext context) { }
}
