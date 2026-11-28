using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Router;
using Netora.Infrastructure.Identity;

namespace Netora.WebApi.Controllers;

/// <summary>
/// Router reads that are run on demand only (09.03, 04.04): the live status of one customer, and the import scan.
/// Router credentials never leave the API.
/// </summary>
[ApiController]
public sealed class RouterController(IRouterStatusService status, IPppoeImportService import, IRouterClient router) : ControllerBase
{
    /// <summary>Reads current traffic counters for a visible customer's running PPPoE connection.</summary>
    [HttpGet("api/v1/customers/{customerId:int}/pppoe-traffic")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(ApiResponse<PppoeTrafficSampleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PppoeTrafficSampleDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Traffic(int customerId, CancellationToken cancellationToken)
    {
        var result = await status.SampleTrafficAsync(customerId, User.IsInRole(Roles.Manager), cancellationToken);
        return result is null
            ? NotFound(ApiResponse<PppoeTrafficSampleDto>.Fail("not_found", "Customer not found.", HttpContext.TraceIdentifier))
            : Ok(ApiResponse<PppoeTrafficSampleDto>.Ok(result, HttpContext.TraceIdentifier));
    }

    [HttpGet("api/v1/customers/{customerId:int}/diagnostics")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Diagnostics(int customerId, bool includeLogs = true, CancellationToken cancellationToken = default)
    {
        var result = await status.DiagnoseAsync(customerId, User.IsInRole(Roles.Manager), includeLogs, cancellationToken);
        return result is null
            ? NotFound(ApiResponse<PppoeDiagnosticsDto>.Fail("not_found", "Customer not found.", HttpContext.TraceIdentifier))
            : Ok(ApiResponse<PppoeDiagnosticsDto>.Ok(result, HttpContext.TraceIdentifier));
    }

    /// <summary>Checks the router now and compares it with SQL Server. 404 for missing and hidden customers alike.</summary>
    [HttpGet("api/v1/customers/{customerId:int}/router-status")]
    [ProducesResponseType(typeof(ApiResponse<RouterStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<RouterStatusDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Status(int customerId, CancellationToken cancellationToken)
    {
        var result = await status.CheckAsync(customerId, User.IsInRole(Roles.Manager), cancellationToken);
        return result is null
            ? NotFound(ApiResponse<RouterStatusDto>.Fail("not_found", "Customer not found.", HttpContext.TraceIdentifier))
            : Ok(ApiResponse<RouterStatusDto>.Ok(result, HttpContext.TraceIdentifier));
    }

    /// <summary>Manager only: lists the router's PPP secrets and whether each is new or already in SQL Server. Imports nothing.</summary>
    [HttpGet("api/v1/pppoe/import/scan")]
    [Authorize(Policy = Policies.ManagerOnly)]
    [ProducesResponseType(typeof(ApiResponse<ImportScanDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ImportScanDto>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Scan(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(ApiResponse<ImportScanDto>.Ok(await import.ScanAsync(cancellationToken), HttpContext.TraceIdentifier));
        }
        catch (RouterException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ApiResponse<ImportScanDto>.Fail("router_error", ex.Message, HttpContext.TraceIdentifier));
        }
    }

    /// <summary>Manager only: imports the selected new secrets. Existing usernames are skipped, never overwritten.</summary>
    [HttpPost("api/v1/pppoe/import")]
    [Authorize(Policy = Policies.ManagerOnly)]
    [ProducesResponseType(typeof(ApiResponse<ImportResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<ImportResultDto>), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse<ImportResultDto>), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Import([FromBody] ImportRequest request, CancellationToken cancellationToken)
    {
        if (request.PreviewMode is not null && request.PreviewMode != router.Mode)
            return Conflict(ApiResponse<ImportResultDto>.Fail("router_mode_changed",
                "The router mode changed after your scan. Scan the router again before importing.", HttpContext.TraceIdentifier));

        var userId = User.FindFirst(NetoraClaimTypes.Subject)?.Value ?? string.Empty;
        try
        {
            var result = await import.ImportAsync(request with { Names = (request.Names ?? []).Take(1000).ToList() }, userId,
                User.FindFirst(NetoraClaimTypes.Name)?.Value ?? userId, cancellationToken);
            return Ok(ApiResponse<ImportResultDto>.Ok(result, HttpContext.TraceIdentifier));
        }
        catch (RouterException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, ApiResponse<ImportResultDto>.Fail("router_error", ex.Message, HttpContext.TraceIdentifier));
        }
    }
}
