namespace Netora.Domain.Customers;

public enum CustomerStatus
{
    Active = 1,
    Disabled = 2,
    Terminated = 3,
}

/// <summary>
/// PROVISIONAL schema. The customer field schema is an open specification; these fields are taken from the
/// approved Customers List and Add/Edit mockups only. Plan and NAP are plain text until modules 05 and 06 add
/// their own tables, at which point they become foreign keys.
/// </summary>
public sealed class Customer
{
    public const string UnknownNap = "UNKNOWN";

    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? MobileNumber { get; set; }
    public string? Address { get; set; }
    public string PppoeUsername { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string NapBox { get; set; } = UnknownNap;
    public string? OnuSerial { get; set; }
    public CustomerStatus Status { get; set; } = CustomerStatus.Active;

    /// <summary>Restricted visibility: only Managers may see this customer, including in counts and searches.</summary>
    public bool IsManagerOnly { get; set; }

    /// <summary>True for development sample rows, so they can be identified and removed.</summary>
    public bool IsSampleData { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
