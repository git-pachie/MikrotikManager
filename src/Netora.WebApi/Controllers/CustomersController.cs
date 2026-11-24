using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Customers;
using Netora.Infrastructure.Identity;

namespace Netora.WebApi.Controllers;

[ApiController]
[Route("api/v1/customers")]
public sealed class CustomersController(ICustomerQueries customers, ICustomerCommands commands) : ControllerBase
{
    /// <summary>
    /// Paged customer list with summary counts. Manager-only customers are excluded for Staff from the rows,
    /// the counts and the filter options. The role is read from the database during token validation.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CustomerListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? search, [FromQuery] string? status, [FromQuery] string? nap, [FromQuery] string? plan,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var query = new CustomerListQuery(Trim(search, 100), Trim(status, 20), Trim(nap, 50), Trim(plan, 100), page, pageSize);
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
        var actorId = User.FindFirst(NetoraClaimTypes.Subject)?.Value;
        if (string.IsNullOrEmpty(actorId))
            return Unauthorized(ApiResponse<CreatedCustomerDto>.Fail("unauthorized", "Authentication is required.", HttpContext.TraceIdentifier));

        var result = await commands.CreateAsync(request, actorId, IsManager, cancellationToken);
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

    private bool IsManager => User.IsInRole(Roles.Manager);

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length > max ? value[..max] : value;
    }
}
