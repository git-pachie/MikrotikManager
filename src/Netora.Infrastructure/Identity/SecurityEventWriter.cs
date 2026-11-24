using System.Text;
using Microsoft.Extensions.Logging;
using Netora.Application.Abstractions;
using Netora.Domain.Security;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Identity;

internal sealed class SecurityEventWriter(NetoraDbContext db, ILogger<SecurityEventWriter> logger) : ISecurityEventWriter
{
    private const int MaxTypedNameLength = 256;

    public async Task WriteAsync(string eventType, bool succeeded, string? userId, string? typedUserName,
        string? ipAddress, string? reason, CancellationToken cancellationToken)
    {
        var name = Sanitize(typedUserName);

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
