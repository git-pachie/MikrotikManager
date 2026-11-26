namespace Netora.Domain.Network;

/// <summary>
/// A NAP box (06.01). Agreed 2026-11-26:
/// <list type="bullet">
/// <item>No port tracking and no capacity limit (the approved mockup puts port-level management outside V1).</item>
/// <item>Managers add and edit NAP boxes; Staff see the list and details. Nothing is ever deleted.</item>
/// <item>UNKNOWN is built in (<see cref="IsSystem"/>): it cannot be edited or deactivated. Imported accounts start there.</item>
/// <item>An inactive NAP cannot be chosen for new assignments; customers already on it stay.</item>
/// </list>
/// </summary>
public sealed class NapBox
{
    public const string UnknownCode = "UNKNOWN";

    public int Id { get; set; }

    /// <summary>Unique, for example NAP-01.</summary>
    public string Code { get; set; } = string.Empty;

    public string? Area { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>True only for UNKNOWN.</summary>
    public bool IsSystem { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string? CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }

    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
