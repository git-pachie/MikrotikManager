using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Audit;
using Netora.Application.Auth;
using Netora.Application.Common;

namespace Netora.WebApi.Controllers;

/// <summary>Audit Trail (12.01). Managers only. Read only.</summary>
[ApiController]
[Route("api/v1/audit")]
[Authorize(Policy = Policies.ManagerOnly)]
public sealed class AuditController(IAuditQueries audit) : ControllerBase
{
    /// <summary>
    /// Newest first. from/to: yyyy-MM-dd (server local days, inclusive); category: one of the Audit Trail categories;
    /// actor and search: text contained; pageSize 25, 50 or 100; at most 200 pages.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<AuditListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuditListDto>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? category,
        [FromQuery] string? actor, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();
        var fromDate = ParseDate(from, nameof(from), errors);
        var toDate = ParseDate(to, nameof(to), errors);
        if (fromDate > toDate) errors[nameof(from)] = ["The start date must be on or before the end date."];
        if (errors.Count > 0)
            return BadRequest(ApiResponse<AuditListDto>.Fail("validation_failed", "One or more validation errors occurred.", HttpContext.TraceIdentifier, errors));

        var result = await audit.ListAsync(new AuditQuery(fromDate, toDate, category, actor, search, page, pageSize), cancellationToken);
        return Ok(ApiResponse<AuditListDto>.Ok(result, HttpContext.TraceIdentifier));
    }

    private static DateOnly? ParseDate(string? value, string field, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateOnly.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        errors[field] = ["Use the date format yyyy-MM-dd."];
        return null;
    }
}
