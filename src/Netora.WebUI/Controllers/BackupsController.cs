using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Operations;
using Netora.WebUI.Services;

namespace Netora.WebUI.Controllers;

[Authorize(Roles = Roles.Manager)]
[Route("backups")]
public sealed class BackupsController(INetoraApiClient api) : Controller
{
    [HttpGet("")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var history = await api.GetBackupHistoryAsync(ct);
        var settings = await api.GetBackupSettingsAsync(ct);
        if (!history.Success || !settings.Success)
            ViewData["Error"] = history.Error?.Message ?? settings.Error?.Message ?? "Backup information could not be loaded.";
        return View(new BackupsViewModel(history.Data, settings.Data));
    }

    [HttpPost("settings")]
    public async Task<IActionResult> Save(string? destination, TimeOnly? dailyTimeSingapore, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData[CustomersController.ErrorKey] = "Enter a valid daily backup time.";
            return RedirectToAction(nameof(Index));
        }
        return Finish(await api.SaveBackupSettingsAsync(new(destination, dailyTimeSingapore), ct), "Backup settings saved.");
    }

    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct)
        => Finish(await api.QueueBackupAsync(ct), "Backup queued. Refresh this page to check the result.");

    private IActionResult Finish(ApiResponse<object> result, string message)
    {
        TempData[result.Success ? CustomersController.ToastKey : CustomersController.ErrorKey] =
            result.Success ? message : result.Error?.Message ?? "The backup request failed.";
        return RedirectToAction(nameof(Index));
    }
}

public sealed record BackupsViewModel(BackupHistoryDto? History, BackupSettingsDto? Settings);
