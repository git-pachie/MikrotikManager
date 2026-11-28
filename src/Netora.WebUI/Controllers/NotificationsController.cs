using Microsoft.AspNetCore.Mvc;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

public sealed class NotificationsController(INetoraApiClient api) : Controller
{
    [HttpGet("/notifications")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var result = await api.GetNotificationsAsync(ct);
        if (!result.Success) ViewData["Error"] = result.Error?.Message ?? "Notifications could not be loaded.";
        return View(result.Data);
    }
}
