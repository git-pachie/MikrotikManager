using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Customers;
using Netora.Application.Pppoe;
using Netora.Infrastructure.Identity;

using Netora.WebApi.Infrastructure;

namespace Netora.WebApi.Controllers;

/// <summary>
/// PPPoE accounts, approval requests and Manager-only deletion.
/// </summary>
[ApiController]
[Route("api/v1/pppoe")]
public sealed class PppoeController(IPppoeQueries queries, IPppoeCommands commands) : ControllerBase
{
    [HttpPost("{customerId:int}/password")]
    [Authorize(Policy = Policies.ManagerOnly)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(ApiResponse<PppoePasswordDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RevealPassword(int customerId, CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        if (actor is null) return Unauthenticated();
        var result = await commands.RevealPasswordAsync(customerId, actor, cancellationToken);
        return result.Value is { } value
            ? Ok(ApiResponse<PppoePasswordDto>.Ok(value, HttpContext.TraceIdentifier))
            : Failure(new(result.Outcome));
    }

    [HttpDelete("{customerId:int}")]
    [Authorize(Policy = Policies.ManagerOnly)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int customerId, [FromBody] DeletePppoeRequest request, CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        if (actor is null) return Unauthenticated();
        var result = await commands.DeleteAsync(customerId, request, actor, cancellationToken);
        if (result.Outcome != PppoeOutcome.Ok || result.Request is not { } deletion) return Failure(result);

        var response = ApiResponse<ApprovalRequestDto>.Ok(deletion, HttpContext.TraceIdentifier);
        return deletion.ProvisioningStatus == "Succeeded" ? Ok(response) : Accepted(response);
    }

    /// <summary>PPPoE accounts with counts. Accounts of Manager-only customers are excluded for Staff, also from the counts.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PppoeListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? state,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null, CancellationToken cancellationToken = default)
    {
        var column = sortBy?.Trim().ToLowerInvariant();
        var query = new PppoeListQuery(
            Trim(search, 100),
            PppoeListQuery.States.Contains(state?.Trim().ToLowerInvariant()) ? state!.Trim().ToLowerInvariant() : null,
            page, pageSize,
            PppoeListQuery.SortColumns.Contains(column) ? column : null,
            string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc");
        var result = await queries.ListAsync(query, IsManager, cancellationToken);
        return Ok(ApiResponse<PppoeListDto>.Ok(result, HttpContext.TraceIdentifier));
    }

    /// <summary>One account. 404 both for a missing customer and for a Manager-only customer requested by Staff.</summary>
    [HttpGet("{customerId:int}")]
    [ProducesResponseType(typeof(ApiResponse<PppoeDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PppoeDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int customerId, CancellationToken cancellationToken)
    {
        var detail = await queries.GetAsync(customerId, IsManager, cancellationToken);
        return detail is null
            ? NotFound(ApiResponse<PppoeDetailDto>.Fail("not_found", "PPPoE account not found.", HttpContext.TraceIdentifier))
            : Ok(ApiResponse<PppoeDetailDto>.Ok(detail, HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Edits PPPoE details. Rejected with 409 account_active while the account is Active (disable it first).
    /// NewPassword is write-only: leave it empty to keep the stored password.
    /// </summary>
    [HttpPut("{customerId:int}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int customerId, [FromBody] UpdatePppoeRequest request, CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        if (actor is null) return Unauthenticated();
        var result = await commands.UpdateAsync(customerId, request, actor, cancellationToken);
        return result.Outcome switch
        {
            PppoeOutcome.Ok => Ok(ApiResponse<object>.Ok(new { changed = true }, HttpContext.TraceIdentifier)),
            PppoeOutcome.NoChanges => Ok(ApiResponse<object>.Ok(new { changed = false }, HttpContext.TraceIdentifier)),
            _ => Failure(result),
        };
    }

    /// <summary>
    /// Requests a change that needs a Manager's approval. Operation: "Disable" (Active), "Enable" (Disabled),
    /// "ChangePlan" with TargetPlan (Active), "Terminate" (Active or Disabled) or "Create" (resubmit a rejected creation).
    /// Staff requests wait for a Manager; a Manager's request is approved at once, except Create, which always waits.
    /// </summary>
    [HttpPost("{customerId:int}/requests")]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Submit(int customerId, [FromBody] SubmitPppoeRequest request, CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        if (actor is null) return Unauthenticated();
        var result = await commands.SubmitAsync(customerId, request, actor, cancellationToken);
        return result.Outcome == PppoeOutcome.Ok
            ? StatusCode(StatusCodes.Status201Created, ApiResponse<ApprovalRequestDto>.Ok(result.Request!, HttpContext.TraceIdentifier))
            : Failure(result);
    }

    /// <summary>Manager only: approve or reject a pending request. A request can be decided once.</summary>
    [HttpPost("{customerId:int}/requests/{requestId:long}/decision")]
    [Authorize(Policy = Policies.ManagerOnly)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<ApprovalRequestDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Decide(int customerId, long requestId, [FromBody] DecidePppoeRequest decision,
        CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        if (actor is null) return Unauthenticated();
        var result = await commands.DecideAsync(customerId, requestId, decision, actor, cancellationToken);
        return result.Outcome == PppoeOutcome.Ok
            ? Ok(ApiResponse<ApprovalRequestDto>.Ok(result.Request!, HttpContext.TraceIdentifier))
            : Failure(result);
    }

    private IActionResult Failure(PppoeResult result) => PppoeResultMapper.Failure(this, result);

    private bool IsManager => User.IsInRole(Roles.Manager);

    private IActionResult Unauthenticated()
        => Unauthorized(ApiResponse<object>.Fail("unauthorized", "Authentication is required.", HttpContext.TraceIdentifier));

    private CustomerActor? CurrentActor()
    {
        var userId = User.FindFirst(NetoraClaimTypes.Subject)?.Value;
        if (string.IsNullOrEmpty(userId)) return null;
        return new CustomerActor(userId, User.FindFirst(NetoraClaimTypes.Name)?.Value ?? userId, IsManager);
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length > max ? value[..max] : value;
    }
}
