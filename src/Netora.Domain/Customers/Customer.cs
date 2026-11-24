namespace Netora.Domain.Customers;

/// <summary>
/// Customer lifecycle. Agreed 2026-11-24: a new customer starts as <see cref="PendingApproval"/> and only a Manager
/// can approve it. Transitions to Active, Disabled and Terminated are implemented with modules 08 (Approvals) and
/// 09 (MikroTik), keeping the approval decision separate from router provisioning.
/// </summary>
public enum CustomerStatus
{
    Active = 1,
    Disabled = 2,
    Terminated = 3,
    PendingApproval = 4,
}

/// <summary>
/// PROVISIONAL schema, taken from the approved Add/Edit Customer form (customer-form.html), approved by the user
/// on 2026-11-24. Plan and NAP are plain text until modules 05 and 06 add their own tables, at which point they
/// become foreign keys.
/// </summary>
public sealed class Customer
{
    public const string UnknownNap = "UNKNOWN";

    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MobileNumber { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Notes { get; set; }
    public string PppoeUsername { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string NapBox { get; set; } = UnknownNap;
    public string? OnuSerial { get; set; }
    public string? OnuModel { get; set; }
    public CustomerStatus Status { get; set; } = CustomerStatus.PendingApproval;

    /// <summary>Restricted visibility: only Managers may see this customer, including in counts and searches.</summary>
    public bool IsManagerOnly { get; set; }

    /// <summary>True for development sample rows, so they can be identified and removed.</summary>
    public bool IsSampleData { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Identity user ID of the creator. Null for sample data.</summary>
    public string? CreatedByUserId { get; set; }
}
