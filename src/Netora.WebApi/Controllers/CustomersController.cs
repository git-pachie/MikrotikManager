using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Customers;
using Netora.Application.Logging;
using Netora.Infrastructure.Identity;

namespace Netora.WebApi.Controllers;

[ApiController]
[Route("api/v1/customers")]
public sealed class CustomersController(ICustomerQueries customers, ICustomerCommands commands, IActivityLog activity) : ControllerBase
{
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Policies.ManagerOnly)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Delete(int id, [FromBody] DeleteCustomerRequest request, CancellationToken cancellationToken)
    {
        var trace = HttpContext.TraceIdentifier;
        var actor = CurrentActor();
        if (actor is null)
            return Unauthorized(ApiResponse<object>.Fail("unauthorized", "Authentication is required.", trace));

        return (await commands.DeleteAsync(id, request, actor, cancellationToken)) switch
        {
            DeleteCustomerOutcome.Deleted => Ok(ApiResponse<object>.Ok(new { }, trace)),
            DeleteCustomerOutcome.NotFound => NotFound(ApiResponse<object>.Fail("not_found", "Customer not found.", trace)),
            DeleteCustomerOutcome.Forbidden => StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<object>.Fail("forbidden", "Only Managers can delete customers.", trace)),
            DeleteCustomerOutcome.Conflict => Conflict(ApiResponse<object>.Fail("concurrency_conflict", "The customer changed. Reload and try again.", trace)),
            DeleteCustomerOutcome.PppoeDeletionRequired => Conflict(ApiResponse<object>.Fail("pppoe_delete_required",
                "Complete PPPoE account deletion in Netora first.", trace)),
            DeleteCustomerOutcome.PppoeExistsOnRouter => Conflict(ApiResponse<object>.Fail("pppoe_exists_on_router",
                "A PPPoE secret for this customer still exists on the configured router. Remove it before retrying.", trace)),
            DeleteCustomerOutcome.RouterUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable,
                ApiResponse<object>.Fail("router_unavailable", "Netora could not verify the PPPoE account on the router. Try again when the router is reachable.", trace)),
            _ => throw new InvalidOperationException("Unexpected customer deletion outcome."),
        };
    }

    /// <summary>
    /// Paged customer list with summary counts. Manager-only customers are excluded for Staff from the rows,
    /// the counts and the filter options. The role is read from the database during token validation.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CustomerListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? nap, [FromQuery] string? plan,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = null, [FromQuery] string? sortDir = null, CancellationToken cancellationToken = default)
    {
        var query = new CustomerListQuery(Trim(search, 100), Trim(status, 20), Trim(nap, 50), Trim(plan, 100), page, pageSize,
            CustomerListQuery.SortColumns.Contains(sortBy?.Trim().ToLowerInvariant()) ? sortBy!.Trim().ToLowerInvariant() : null,
            string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc");
        var result = await customers.ListAsync(query, includeManagerOnly: IsManager, cancellationToken);
        return Ok(ApiResponse<CustomerListDto>.Ok(result, HttpContext.TraceIdentifier));
    }

    /// <summary>Plan and NAP choices for the Add Customer form. Provisional until the Plans and NAP modules exist.</summary>
    [HttpGet("form-options")]
    [ProducesResponseType(typeof(ApiResponse<CustomerFormOptionsDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> FormOptions(CancellationToken cancellationToken)
    {
        var options = await customers.GetFormOptionsAsync(includeManagerOnly: IsManager, cancellationToken);
        return Ok(ApiResponse<CustomerFormOptionsDto>.Ok(options, HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// Creates a customer in the Pending Approval state. Staff and Managers may create; only Managers may set
    /// the Manager-only flag. Nothing is sent to MikroTik.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreatedCustomerDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<CreatedCustomerDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<CreatedCustomerDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<CreatedCustomerDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateCustomerRequest request, CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        if (actor is null)
            return Unauthorized(ApiResponse<CreatedCustomerDto>.Fail("unauthorized", "Authentication is required.", HttpContext.TraceIdentifier));

        var result = await commands.CreateAsync(request, actor, cancellationToken);
        var traceId = HttpContext.TraceIdentifier;
        return result.Outcome switch
        {
            CreateCustomerOutcome.Created => StatusCode(StatusCodes.Status201Created,
                ApiResponse<CreatedCustomerDto>.Ok(new CreatedCustomerDto(result.CustomerId!.Value, "PendingApproval"), traceId)),
            CreateCustomerOutcome.ValidationFailed => BadRequest(ApiResponse<CreatedCustomerDto>.Fail(
                "validation_failed", "One or more validation errors occurred.", traceId, result.Errors)),
            CreateCustomerOutcome.DuplicatePppoeUsername => Conflict(ApiResponse<CreatedCustomerDto>.Fail(
                "duplicate_pppoe_username", "This PPPoE Username is already in use.", traceId, result.Errors)),
            CreateCustomerOutcome.ManagerOnlyNotAllowed => StatusCode(StatusCodes.Status403Forbidden,
                ApiResponse<CreatedCustomerDto>.Fail("forbidden", "Only Managers can restrict customer visibility.", traceId)),
            _ => throw new InvalidOperationException($"Unhandled outcome {result.Outcome}"),
        };
    }

    /// <summary>
    /// One customer with its activity history. Returns 404 both when the customer does not exist and when it is
    /// Manager-only and the caller is Staff, so Staff cannot learn that a restricted customer exists.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<CustomerDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CustomerDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken)
    {
        var detail = await customers.GetDetailAsync(id, viewerIsManager: IsManager, cancellationToken);
        var traceId = HttpContext.TraceIdentifier;
        if (detail is null)
        {
            // Staff still get a plain 404; only the server log records that the Manager-only rule applied (18.02).
            if (!IsManager && await customers.IsManagerOnlyAsync(id, cancellationToken))
                activity.Write(new ActivityEvent(ActivityCategories.Customers, "CustomerAccessDenied", ActivityOutcomes.Denied,
                    ActivityTargets.Customer(id), "ManagerOnly", ActivityLevel.Warning));
            return NotFound(ApiResponse<CustomerDetailDto>.Fail("not_found", "Customer not found.", traceId));
        }
        return Ok(ApiResponse<CustomerDetailDto>.Ok(detail, traceId));
    }

    /// <summary>
    /// Edits a customer's profile. Status is not editable here (approvals and router provisioning own it).
    /// PPPoE Username and Internet Plan of an Active or Disabled customer are locked, and Terminated customers are
    /// read-only. Send the ConcurrencyStamp read with the customer; a stale stamp returns 409.
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<UpdatedCustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UpdatedCustomerDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UpdatedCustomerDto>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<UpdatedCustomerDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<UpdatedCustomerDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateCustomerRequest request, CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        if (actor is null)
            return Unauthorized(ApiResponse<UpdatedCustomerDto>.Fail("unauthorized", "Authentication is required.", HttpContext.TraceIdentifier));

        var result = await commands.UpdateAsync(id, request, actor, cancellationToken);
        var traceId = HttpContext.TraceIdentifier;
        switch (result.Outcome)
        {
            case UpdateCustomerOutcome.Updated:
            case UpdateCustomerOutcome.NoChanges:
                var detail = await customers.GetDetailAsync(id, IsManager, cancellationToken);
                return Ok(ApiResponse<UpdatedCustomerDto>.Ok(
                    new UpdatedCustomerDto(id, detail?.ConcurrencyStamp ?? string.Empty, result.Outcome == UpdateCustomerOutcome.Updated), traceId));
            case UpdateCustomerOutcome.ValidationFailed:
                return BadRequest(ApiResponse<UpdatedCustomerDto>.Fail(
                    "validation_failed", "One or more validation errors occurred.", traceId, result.Errors));
            case UpdateCustomerOutcome.NotFound:
                return NotFound(ApiResponse<UpdatedCustomerDto>.Fail("not_found", "Customer not found.", traceId));
            case UpdateCustomerOutcome.Locked:
                return Conflict(ApiResponse<UpdatedCustomerDto>.Fail(
                    "customer_locked", "Terminated customers are read-only.", traceId));
            case UpdateCustomerOutcome.ServiceFieldsLocked:
                return Conflict(ApiResponse<UpdatedCustomerDto>.Fail(
                    "service_fields_locked", "PPPoE Username and Internet Plan can only be changed through a Manager-approved request.", traceId, result.Errors));
            case UpdateCustomerOutcome.DuplicatePppoeUsername:
                return Conflict(ApiResponse<UpdatedCustomerDto>.Fail(
                    "duplicate_pppoe_username", "This PPPoE Username is already in use.", traceId, result.Errors));
            case UpdateCustomerOutcome.ManagerOnlyNotAllowed:
                return StatusCode(StatusCodes.Status403Forbidden,
                    ApiResponse<UpdatedCustomerDto>.Fail("forbidden", "Only Managers can restrict customer visibility.", traceId));
            case UpdateCustomerOutcome.Conflict:
                return Conflict(ApiResponse<UpdatedCustomerDto>.Fail(
                    "concurrency_conflict", "This customer was changed by someone else. Reload and try again.", traceId));
            default:
                throw new InvalidOperationException($"Unhandled outcome {result.Outcome}");
        }
    }

    /// <summary>
    /// Moves a customer to another NAP box (06.02): Staff and Managers, no approval and no router call. The target must
    /// be active (or UNKNOWN). Same visibility and termination rules as editing; a stale stamp returns 409.
    /// </summary>
    [HttpPut("{id:int}/nap")]
    [ProducesResponseType(typeof(ApiResponse<UpdatedCustomerDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UpdatedCustomerDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UpdatedCustomerDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<UpdatedCustomerDto>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MoveNap(int id, [FromBody] MoveNapRequest request, CancellationToken cancellationToken)
    {
        var actor = CurrentActor();
        if (actor is null)
            return Unauthorized(ApiResponse<UpdatedCustomerDto>.Fail("unauthorized", "Authentication is required.", HttpContext.TraceIdentifier));

        var result = await commands.MoveNapAsync(id, request, actor, cancellationToken);
        var traceId = HttpContext.TraceIdentifier;
        return result.Outcome switch
        {
            UpdateCustomerOutcome.Updated or UpdateCustomerOutcome.NoChanges => Ok(ApiResponse<UpdatedCustomerDto>.Ok(
                new UpdatedCustomerDto(id, string.Empty, result.Outcome == UpdateCustomerOutcome.Updated), traceId)),
            UpdateCustomerOutcome.ValidationFailed => BadRequest(ApiResponse<UpdatedCustomerDto>.Fail(
                "validation_failed", "One or more validation errors occurred.", traceId, result.Errors)),
            UpdateCustomerOutcome.NotFound => NotFound(ApiResponse<UpdatedCustomerDto>.Fail("not_found", "Customer not found.", traceId)),
            UpdateCustomerOutcome.Locked => Conflict(ApiResponse<UpdatedCustomerDto>.Fail("customer_locked", "Terminated customers are read-only.", traceId)),
            UpdateCustomerOutcome.Conflict => Conflict(ApiResponse<UpdatedCustomerDto>.Fail(
                "concurrency_conflict", "This customer was changed by someone else. Reload and try again.", traceId)),
            _ => throw new InvalidOperationException($"Unhandled outcome {result.Outcome}"),
        };
    }

    private CustomerActor? CurrentActor()
    {
        var userId = User.FindFirst(NetoraClaimTypes.Subject)?.Value;
        if (string.IsNullOrEmpty(userId)) return null;
        return new CustomerActor(userId, User.FindFirst(NetoraClaimTypes.Name)?.Value ?? userId, IsManager);
    }

    private bool IsManager => User.IsInRole(Roles.Manager);

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length > max ? value[..max] : value;
    }
}
