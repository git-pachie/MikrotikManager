using Microsoft.Extensions.Logging;
using Netora.Application.Customers;
using Netora.Domain.Audit;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Catalog;

/// <summary>Writes <see cref="AuditEvent"/> rows for plan and NAP changes (12.01).</summary>
internal static class AuditTrail
{
    public static AuditEvent New(CustomerActor actor, string entityType, int entityId, string entityName, string action,
        string? detail, string outcome = "Completed") => new()
    {
        OccurredAtUtc = DateTimeOffset.UtcNow,
        ActorUserId = actor.UserId,
        ActorUserName = actor.UserName,
        EntityType = entityType,
        EntityId = entityId,
        EntityName = Cut(entityName, 100),
        Action = action,
        Detail = detail is null ? null : Cut(detail, 500),
        Outcome = outcome,
    };

    /// <summary>
    /// Saves one record after the change itself was saved (used when the ID was only known after the save, or when the
    /// change happened on the router). A failure is logged and does not undo or fail the change.
    /// </summary>
    public static async Task RecordAsync(NetoraDbContext db, ILogger logger, AuditEvent entry, CancellationToken ct)
    {
        try
        {
            db.AuditEvents.Add(entry);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.Entry(entry).State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            logger.LogError(ex, "AuditRecordFailed Action={Action} EntityType={EntityType} EntityId={EntityId}", entry.Action, entry.EntityType, entry.EntityId);
        }
    }

    private static string Cut(string v, int max) => v.Length <= max ? v : v[..max];
}
