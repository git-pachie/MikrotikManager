using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Approvals;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Customers;
using Netora.Application.Pppoe;
using Netora.Infrastructure.Identity;
using Netora.WebApi.Infrastructure;

namespace Netora.WebApi.Controllers;

/// <summary>
/// Approval requests across all customers (08.02). Staff can list and view requests (not those of Manager-only
/// customers); only Managers decide. Requests are created through <c>POST /api/v1/pppoe/{id}/requests</c>
/// and automatically when a customer is added.
/// </summary>
[ApiController]
[Route("api/v1/approvals")]
public sealed class ApprovalsController(IApprovalQueries queries, IApprovalCommands commands) : ControllerBase
{
    /// <summary>Requests with counts per status. status: pending (default), approved, rejected or all.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ApprovalListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? operation, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var s = status?.Trim().ToLowerInvariant();
        var query = new ApprovalListQuery(
            ApprovalListQuery.Statuses.Contains(s) ? s : "pending",
            PppoeStates.TryParseOperation(operation, out _) ? operation!.Trim() : null,
            string.IsNullOrWhiteSpace(search) ? null : search.Trim()[..Math.Min(100, search.Trim().Length)],
            Math.Max(1, page), pageSize);
        var result = await queries.ListAsync(query, IsManager, cancellationToken);
        return Ok(ApiResponse<ApprovalListDto>.Ok(result, HttpContext.TraceIdentifier));
    }

    /// <summary>One request with its customer, before/after, decision and router status. 404 for hidden and missing requests alike.</summary>
    [HttpGet("{requestId:long}")]
    [ProducesResponseType(typeof(ApiResponse<ApprovalDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(long requestId, CancellationToken cancellationToken)
    {
        var detail = await queries.GetAsync(requestId, IsManager, cancellationToken);
        return detail is null
            ? NotFound(ApiResponse<ApprovalDetailDto>.Fail("not_found", "Approval request not found.", HttpContext.TraceIdentifier))
            : Ok(ApiResponse<ApprovalDetailDto>.Ok(detail, HttpContext.TraceIdentifier));
    }

    /// <summary>Manager only: approve or reject a pending request. A request is decided once; a second decision returns 409.</summary>
    [HttpPost("{requestId:long}/decision")]
    [Authorize(Policy = Policies.ManagerOnly)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Decide(long requestId, [FromBody] DecidePppoeRequest decision, CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(NetoraClaimTypes.Subject)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(ApiResponse<object>.Fail("unauthorized", "Authentication is required.", HttpContext.TraceIdentifier));
        var actor = new CustomerActor(userId, User.FindFirst(NetoraClaimTypes.Name)?.Value ?? userId, IsManager);

        var result = await commands.DecideAsync(requestId, decision, actor, cancellationToken);
        return result.Outcome == PppoeOutcome.Ok
            ? Ok(ApiResponse<ApprovalRequestDto>.Ok(result.Request!, HttpContext.TraceIdentifier))
            : PppoeResultMapper.Failure(this, result, "Approval request not found.");
    }

    private bool IsManager => User.IsInRole(Roles.Manager);
}
