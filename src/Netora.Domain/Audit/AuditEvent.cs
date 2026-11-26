namespace Netora.Domain.Audit;

/// <summary>
/// An audit record for changes that have no history table of their own: Internet Plans and NAP Boxes (12.01).
/// Customer, approval and router steps are already in <c>CustomerActivities</c>, sign-in and administration in
/// <c>SecurityEvents</c>; the Audit Trail page reads all three. Rows are append-only and never deleted.
/// </summary>
public sealed class AuditEvent
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string ActorUserId { get; set; } = string.Empty;

    /// <summary>User name at the time, so the trail stays readable if the account changes later.</summary>
    public string ActorUserName { get; set; } = string.Empty;

    /// <summary>"Plan" or "NapBox".</summary>
    public string EntityType { get; set; } = string.Empty;

    public int EntityId { get; set; }

    /// <summary>Plan name or NAP code at the time of the change.</summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>Stable action name, for example PlanCreated, PlanUpdated, NapDeactivated, ProfileSentToRouter.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Changed field names or a short result. Never values that could be personal data.</summary>
    public string? Detail { get; set; }

    public string Outcome { get; set; } = "Completed";
}

public static class AuditEntityTypes
{
    public const string Plan = "Plan";
    public const string NapBox = "NapBox";
}
