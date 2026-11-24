using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Customers;

namespace Netora.WebApi.Controllers;

[ApiController]
[Route("api/v1/customers")]
public sealed class CustomersController(ICustomerQueries customers) : ControllerBase
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
        var result = await customers.ListAsync(query, includeManagerOnly: User.IsInRole(Roles.Manager), cancellationToken);
        return Ok(ApiResponse<CustomerListDto>.Ok(result, HttpContext.TraceIdentifier));
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length > max ? value[..max] : value;
    }
}
