using System.Text;
using Microsoft.Extensions.Logging;
using Netora.Application.Abstractions;
using Netora.Application.Logging;
using Netora.Domain.Security;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Identity;

internal sealed class SecurityEventWriter(NetoraDbContext db, ILogger<SecurityEventWriter> logger, IActivityLog? activity = null)
    : ISecurityEventWriter
{
    private const int MaxTypedNameLength = 256;

    public async Task WriteAsync(string eventType, bool succeeded, string? userId, string? typedUserName,
        string? ipAddress, string? reason, CancellationToken cancellationToken)
    {
        var name = Sanitize(typedUserName);

        // File log line (18.02). Failed logins keep the user name exactly as typed (cleaned and capped, 18.04).
        (activity ?? NullActivityLog.Instance).Write(ToActivity(eventType, succeeded, userId, name, reason));

        // Structured log line first, so the event is visible even if the database write fails.
        logger.LogInformation(
            "SecurityEvent {EventType} Succeeded={Succeeded} UserId={UserId} UserName={UserName} Ip={IpAddress} Reason={Reason}",
            eventType, succeeded, userId, name, ipAddress, reason);

        try
        {
            db.SecurityEvents.Add(new SecurityEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                EventType = eventType,
                Succeeded = succeeded,
                UserId = userId,
                UserName = name,
                IpAddress = ipAddress,
                Reason = reason,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not persist security event {EventType}", eventType);
        }
    }

    /// <summary>
    /// Maps a security event to its file log line. Sign-in events have no signed-in user yet, so the actor is the
    /// typed user name. For user administration the actor is the signed-in Manager and the target the changed account.
    /// </summary>
    internal static ActivityEvent ToActivity(string eventType, bool succeeded, string? userId, string? userName, string? reason)
    {
        var outcome = succeeded ? ActivityOutcomes.Success : ActivityOutcomes.Failure;
        var target = string.IsNullOrEmpty(userId) ? null : ActivityTargets.User(userId);
        var level = succeeded ? ActivityLevel.Information : ActivityLevel.Warning;
        return eventType switch
        {
            SecurityEventTypes.LoginSucceeded or SecurityEventTypes.LoginFailed or SecurityEventTypes.LockoutTriggered
                => new(ActivityCategories.Authentication, eventType, outcome, target, reason, level,
                    Actor: string.IsNullOrEmpty(userName) ? "anonymous" : userName),
            SecurityEventTypes.Logout or SecurityEventTypes.PasswordChanged or SecurityEventTypes.ProfileUpdated
                => new(ActivityCategories.Authentication, eventType, outcome, target, reason, level, Actor: userName),
            SecurityEventTypes.SettingsUpdated
                => new(ActivityCategories.Settings, eventType, outcome, Detail: reason, Level: level),
            // UserCreated, UserUpdated, RoleChanged, UserDeactivated, UserReactivated, PasswordResetByManager, ManagerBootstrapped.
            _ => new(ActivityCategories.Users, eventType, outcome, target, StripActor(reason), level),
        };
    }

    /// <summary>User admin reasons end with ", by {actor}"; the actor already has its own field.</summary>
    private static string? StripActor(string? reason)
    {
        if (reason is null) return null;
        var i = reason.LastIndexOf("by ", StringComparison.Ordinal);
        if (i == 0) return null;
        return i > 2 && reason[i - 2] == ',' ? reason[..(i - 2)] : reason;
    }

    /// <summary>Removes control characters and line breaks and caps the length, so typed input cannot forge log lines.</summary>
    internal static string? Sanitize(string? value)
    {
        if (value is null) return null;
        var sb = new StringBuilder(Math.Min(value.Length, MaxTypedNameLength));
        foreach (var ch in value)
        {
            if (char.IsControl(ch)) continue;
            sb.Append(ch);
            if (sb.Length >= MaxTypedNameLength) break;
        }
        return sb.ToString();
    }
}
