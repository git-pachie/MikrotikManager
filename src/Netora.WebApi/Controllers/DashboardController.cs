using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Operations;

namespace Netora.WebApi.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public sealed class DashboardController(IDashboardQueries dashboard) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(ApiResponse<DashboardDto>.Ok(await dashboard.GetAsync(User.IsInRole(Roles.Manager), cancellationToken),
            HttpContext.TraceIdentifier));
}
