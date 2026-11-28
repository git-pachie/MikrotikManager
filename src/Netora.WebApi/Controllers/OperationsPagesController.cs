using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Netora.Application.Auth;
using Netora.Application.Common;
using Netora.Application.Operations;
using Netora.Infrastructure.Identity;

namespace Netora.WebApi.Controllers;

[ApiController]
[Route("api/v1")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class OperationsPagesController(IOperationsPages pages, IBackupManager backups) : ControllerBase
{
    private bool IsManager => User.IsInRole(Roles.Manager);
    private string UserId => User.FindFirst(NetoraClaimTypes.Subject)?.Value ?? string.Empty;

    [HttpGet("reports")]
    public async Task<IActionResult> Report(ReportType type = ReportType.CustomerStatus,
        ReportRange range = ReportRange.CurrentMonth, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(type) || !Enum.IsDefined(range))
            return BadRequest(ApiResponse<OperationsReportDto>.Fail("invalid_report_filters", "Choose a supported report and date range.", HttpContext.TraceIdentifier));
        return Ok(ApiResponse<OperationsReportDto>.Ok(await pages.ReportAsync(type, range, IsManager, ct), HttpContext.TraceIdentifier));
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications(CancellationToken ct)
        => Ok(ApiResponse<IReadOnlyList<NotificationItemDto>>.Ok(
            await pages.NotificationsAsync(UserId, IsManager, ct), HttpContext.TraceIdentifier));

    [HttpGet("backups")]
    [Authorize(Policy = Policies.ManagerOnly)]
    public async Task<IActionResult> Backups(CancellationToken ct)
        => Ok(ApiResponse<BackupHistoryDto>.Ok(await pages.BackupsAsync(ct), HttpContext.TraceIdentifier));

    [HttpGet("backups/settings")]
    [Authorize(Policy = Policies.ManagerOnly)]
    public async Task<IActionResult> BackupSettings(CancellationToken ct)
        => Ok(ApiResponse<BackupSettingsDto>.Ok(await backups.GetSettingsAsync(ct), HttpContext.TraceIdentifier));

    [HttpPut("backups/settings")]
    [Authorize(Policy = Policies.ManagerOnly)]
    public async Task<IActionResult> SaveBackupSettings(UpdateBackupSettingsRequest request, CancellationToken ct)
    {
        var error = await backups.SaveSettingsAsync(request, UserId, ct);
        return error is null ? Ok(ApiResponse<object>.Ok(new { saved = true }, HttpContext.TraceIdentifier))
            : BadRequest(ApiResponse<object>.Fail("backup_settings_error", error, HttpContext.TraceIdentifier));
    }

    [HttpPost("backups/run")]
    [Authorize(Policy = Policies.ManagerOnly)]
    public async Task<IActionResult> QueueBackup(CancellationToken ct)
    {
        var error = await backups.QueueAsync(UserId, ct);
        return error is null ? Accepted(ApiResponse<object>.Ok(new { queued = true }, HttpContext.TraceIdentifier))
            : Conflict(ApiResponse<object>.Fail("backup_unavailable", error, HttpContext.TraceIdentifier));
    }
}
