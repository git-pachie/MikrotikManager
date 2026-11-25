namespace Netora.Domain.Customers;

/// <summary>
/// Customer lifecycle. Agreed 2026-11-24: a new customer starts as <see cref="PendingApproval"/> and only a Manager
/// can approve it. Transitions to Active, Disabled and Terminated are implemented with modules 08 (Approvals) and
/// 09 (MikroTik), keeping the approval decision separate from router provisioning.
/// </summary>
/// <summary>
/// Router provisioning state, kept separate from the approval decision (08.04). Without a router connection every
/// account stays <see cref="NotSent"/>.
/// </summary>
public enum RouterSyncStatus
{
    NotSent = 0,
    Synced = 1,
    Failed = 2,
}

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

    public DateTimeOffset? UpdatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }

    /// <summary>
    /// Optimistic concurrency token. Replaced on every write, so an edit made from a stale copy of the form is
    /// rejected instead of silently overwriting a colleague's change.
    /// </summary>
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Agreed 2026-11-24: Terminated customers are view-only for Staff; Managers may still edit their details and
    /// attachments. Nothing is deleted on termination (03.04).
    /// </summary>
    public bool CanBeChangedBy(bool actorIsManager) => Status != CustomerStatus.Terminated || actorIsManager;

    /// <summary>
    /// Agreed 2026-11-25: an Active PPPoE account must be disabled first (Manager-approved) before its PPPoE details,
    /// including the PPPoE Username and the Internet Plan, can change. Terminated accounts keep them fixed: the
    /// disabled secret still exists on the router and history must stay traceable.
    /// </summary>
    public bool ServiceFieldsLocked => Status is CustomerStatus.Active or CustomerStatus.Terminated;

    /// <summary>
    /// Whether the PPPoE details (password, addresses, router comment, and username and plan) may be edited.
    /// Agreed 2026-11-25: Staff and Managers, except while Active; Terminated follows <see cref="CanBeChangedBy"/>
    /// and keeps username and plan fixed.
    /// </summary>
    public bool PppoeDetailsEditableBy(bool actorIsManager) => Status != CustomerStatus.Active && CanBeChangedBy(actorIsManager);

    // ----- PPPoE account (module 04). Stored now, sent to MikroTik after Manager approval (modules 08/09). -----

    /// <summary>
    /// PPPoE secret password, encrypted with ASP.NET Core Data Protection. Never returned to clients; needed later to
    /// create the secret on the router.
    /// </summary>
    public string? PppoePasswordProtected { get; set; }

    /// <summary>Optional static remote address (RouterOS "remote-address").</summary>
    public string? PppoeRemoteAddress { get; set; }

    /// <summary>Optional local address (RouterOS "local-address").</summary>
    public string? PppoeLocalAddress { get; set; }

    /// <summary>RouterOS secret comment.</summary>
    public string? PppoeComment { get; set; }

    /// <summary>Whether the router matches the database. Always NotSent until module 09 connects a router.</summary>
    public RouterSyncStatus RouterSyncStatus { get; set; } = RouterSyncStatus.NotSent;

    /// <summary>RouterOS ".id" of the PPP secret (for example "*1A"), set once the secret is created or imported.</summary>
    public string? RouterSecretId { get; set; }

    /// <summary>"NET-0001" style label shown in the UI. Derived from the ID, so it needs no extra column.</summary>
    public static string DisplayId(int id) => $"NET-{id:D4}";
}

/// <summary>
/// One line of a customer's activity history (Customer Details, Activity tab). Stores who did what and when, with
/// changed field names only. Never customer values such as names, phone numbers or addresses (18-logging.md).
/// Rows are append-only and are kept after termination (03.04).
/// </summary>
public sealed class CustomerActivity
{
    public long Id { get; set; }

    public int CustomerId { get; set; }

    /// <summary>Stable event name, for example CustomerCreated or CustomerUpdated.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Extra context, for example the changed field names. No customer values.</summary>
    public string? Detail { get; set; }

    public string ActorUserId { get; set; } = string.Empty;

    /// <summary>User name at the time of the action, so history stays readable if the account changes later.</summary>
    public string ActorUserName { get; set; } = string.Empty;

    public string Outcome { get; set; } = "Completed";

    public DateTimeOffset OccurredAtUtc { get; set; }
}

/// <summary>
/// A file attached to a customer (03.05). The file itself lives in the configured storage folder under
/// <see cref="StoredFileName"/>; this row holds the metadata. Deleting only hides the row and keeps the file, so
/// attachments stay retained for history and audit (03.04).
/// </summary>
public sealed class CustomerAttachment
{
    public long Id { get; set; }

    public int CustomerId { get; set; }

    /// <summary>Cleaned original name, shown to users and used as the download name. Never used as a path.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Generated name inside the storage folder ({customerId}/{guid}.{ext}). Never taken from user input.</summary>
    public string StoredFileName { get; set; } = string.Empty;

    /// <summary>Detected from the file content, not from what the browser claimed.</summary>
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string UploadedByUserId { get; set; } = string.Empty;

    public string UploadedByUserName { get; set; } = string.Empty;

    public DateTimeOffset UploadedAtUtc { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset? DeletedAtUtc { get; set; }

    public string? DeletedByUserId { get; set; }

    public string? DeletedByUserName { get; set; }
}
