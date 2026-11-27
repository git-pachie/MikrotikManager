using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Customers;
using Netora.Application.Operations;
using Netora.Infrastructure.Identity;

namespace Netora.WebApi.Controllers;

[ApiController]
[Route("api/v1/onu-history")]
public sealed class OnuController(IOnuService onu) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(int? customerId, string? search, int page = 1,
        CancellationToken cancellationToken = default)
    {
        var data = await onu.ListAsync(customerId, search, page, User.IsInRole(Roles.Manager), cancellationToken);
        return data is null
            ? NotFound(ApiResponse<OnuHistoryDto>.Fail("not_found", "Customer not found.", HttpContext.TraceIdentifier))
            : Ok(ApiResponse<OnuHistoryDto>.Ok(data, HttpContext.TraceIdentifier));
    }

    [HttpPost("{customerId:int}/replacements")]
    public async Task<IActionResult> Replace(int customerId, ReplaceOnuRequest request, CancellationToken cancellationToken)
    {
        var actorId = User.FindFirst(NetoraClaimTypes.Subject)?.Value;
        if (actorId is null) return Unauthorized();
        var actor = new CustomerActor(actorId, User.Identity?.Name ?? actorId, User.IsInRole(Roles.Manager));
        var result = await onu.ReplaceAsync(customerId, request, actor, cancellationToken);
        var trace = HttpContext.TraceIdentifier;
        return result.Outcome switch
        {
            UpdateCustomerOutcome.Updated => Ok(ApiResponse<object>.Ok(new { }, trace)),
            UpdateCustomerOutcome.NotFound => NotFound(ApiResponse<object>.Fail("not_found", "Customer not found.", trace)),
            UpdateCustomerOutcome.Locked => Conflict(ApiResponse<object>.Fail("customer_locked", "Terminated customers are read-only for Staff.", trace)),
            UpdateCustomerOutcome.Conflict => Conflict(ApiResponse<object>.Fail("concurrency_conflict", "This customer changed. Reload and try again.", trace)),
            _ => BadRequest(ApiResponse<object>.Fail("validation_failed", "Check the replacement details.", trace, result.Errors)),
        };
    }
}
