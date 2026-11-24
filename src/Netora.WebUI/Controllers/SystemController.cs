using Microsoft.AspNetCore.Mvc;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

/// <summary>Diagnostic JSON endpoint proving the WebUI → Web API → database path. Has no view.</summary>
[Route("system")]
public sealed class SystemController(INetoraApiClient apiClient) : Controller
{
    [HttpGet("api-health")]
    public async Task<IActionResult> ApiHealth(CancellationToken cancellationToken)
    {
        var result = await apiClient.GetHealthAsync(cancellationToken);
        return StatusCode(result.Success ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable, result);
    }
}
