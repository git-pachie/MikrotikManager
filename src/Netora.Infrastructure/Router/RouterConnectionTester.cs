using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Netora.Application.Abstractions;
using Netora.Application.Logging;
using Netora.Application.Router;
using Netora.Domain.Security;
using Netora.Domain.Settings;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Router;

/// <summary>Settings "Test Connection": reads the router identity and version, and remembers the last result.</summary>
internal sealed class RouterConnectionTester(
    NetoraDbContext db,
    IRouterClient router,
    ISecurityEventWriter events,
    ILogger<RouterConnectionTester> logger,
    IActivityLog? activity = null) : IRouterConnectionTester
{
    public async Task<RouterTestResultDto> TestAsync(string actorUserId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        bool ok;
        string message;
        string? errorKind = null;
        try
        {
            var id = await router.GetIdentityAsync(cancellationToken);
            ok = true;
            message = $"Connected to {id.Identity}, RouterOS {id.Version ?? "unknown version"}" + (id.Board is null ? "" : $" ({id.Board})") + ".";
        }
        catch (RouterException ex)
        {
            ok = false;
            message = ex.Message;
            errorKind = ex.Kind.ToString();
        }

        // File log: result and error kind only; the router's message may name hosts and is kept on the Settings page.
        (activity ?? NullActivityLog.Instance).Write(new ActivityEvent(ActivityCategories.MikroTik, "ConnectionTested",
            ok ? ActivityOutcomes.Success : ActivityOutcomes.Failure,
            Detail: ok ? $"mode={router.Mode}" : $"kind={errorKind} mode={router.Mode}",
            Level: ok ? ActivityLevel.Information : ActivityLevel.Warning));

        await SetAsync(AppSettingKeys.RouterLastTestAtUtc, now.ToString("O", CultureInfo.InvariantCulture), actorUserId, now, cancellationToken);
        await SetAsync(AppSettingKeys.RouterLastTestOk, ok ? "true" : "false", actorUserId, now, cancellationToken);
        await SetAsync(AppSettingKeys.RouterLastTestMessage, message.Length <= 500 ? message : message[..500], actorUserId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await events.WriteAsync(SecurityEventTypes.SettingsUpdated, ok, actorUserId, null, null, ok ? "Router test passed" : "Router test failed", cancellationToken);
        logger.LogInformation("RouterConnectionTest Ok={Ok} Mode={Mode} ActorUserId={ActorUserId}", ok, router.Mode, actorUserId);
        return new RouterTestResultDto(ok, router.Mode, message, now);
    }

    private async Task SetAsync(string key, string value, string actor, DateTimeOffset now, CancellationToken ct)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) db.AppSettings.Add(new AppSetting { Key = key, Value = value, UpdatedAtUtc = now, UpdatedByUserId = actor });
        else { row.Value = value; row.UpdatedAtUtc = now; row.UpdatedByUserId = actor; }
    }
}
