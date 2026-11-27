using Microsoft.AspNetCore.Mvc;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

public sealed class DashboardController(INetoraApiClient api) : Controller
{
    [HttpGet("/dashboard")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var result = await api.GetDashboardAsync(cancellationToken);
        return View(result.Data);
    }
}
