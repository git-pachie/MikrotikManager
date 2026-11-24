using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Common;
using Netora.Application.Health;

namespace Netora.WebApi.Controllers;

[ApiController]
[Route("api/v1/health")]
public sealed class HealthController(IHealthService healthService) : ControllerBase
{
    /// <summary>Reports API and database reachability. Returns 503 when the database is unreachable.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<HealthStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<HealthStatusDto>), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var status = await healthService.GetStatusAsync(cancellationToken);
        var body = ApiResponse<HealthStatusDto>.Ok(status, HttpContext.TraceIdentifier);
        return status.DatabaseReachable ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }
}
