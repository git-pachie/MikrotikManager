namespace Netora.Domain.Plans;

/// <summary>
/// An internet plan from the catalogue (05.01) and the MikroTik PPP profile it maps to. Agreed 2026-11-26:
/// <list type="bullet">
/// <item>Managers add, edit and deactivate plans; Staff see the list. Nothing is ever deleted.</item>
/// <item>Netora only maps to a profile that already exists on the router; it never creates or changes profiles.</item>
/// <item>An inactive plan cannot be chosen for new customers or plan changes; customers already on it keep it.</item>
/// <item>Moving a customer to another plan still needs a Manager-approved Change Plan request (05.02).</item>
/// </list>
/// </summary>
public sealed class InternetPlan
{
    public int Id { get; set; }

    /// <summary>Unique, shown everywhere a plan is chosen.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Pesos per month. Empty until a Manager enters it (plans created from existing data had none).</summary>
    public decimal? MonthlyFee { get; set; }

    public int? DownloadMbps { get; set; }

    public int? UploadMbps { get; set; }

    /// <summary>RouterOS PPP profile name sent as the secret's "profile".</summary>
    public string RouterProfile { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public string? CreatedByUserId { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }

    /// <summary>Replaced on every save, so a stale edit form is rejected instead of overwriting.</summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
