namespace Netora.Domain.Approvals;

/// <summary>Operations that need a Manager's approval (08.01).</summary>
public enum ApprovalOperation
{
    /// <summary>Suspend: Active to Disabled.</summary>
    DisablePppoe = 1,

    /// <summary>Reactivate: Disabled to Active.</summary>
    EnablePppoe = 2,

    /// <summary>Create the PPPoE account for a new customer: Pending Approval to Active. Opened automatically on create.</summary>
    CreatePppoe = 3,

    /// <summary>Change the Internet Plan (PPP profile) of an Active or Disabled account.</summary>
    ChangePlan = 4,

    /// <summary>Terminate an Active or Disabled account. Nothing is deleted (03.04).</summary>
    TerminatePppoe = 5,
}

public enum ApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>
/// Router outcome of an approved request, kept apart from the approval decision (08.04).
/// Nothing is sent until the MikroTik module (09) exists, so approved requests stay <see cref="NotSent"/>.
/// </summary>
public enum ProvisioningStatus
{
    /// <summary>Pending or rejected: there is nothing to send.</summary>
    NotApplicable = 0,
    NotSent = 1,
    Succeeded = 2,
    Failed = 3,
}

/// <summary>
/// A request for a sensitive PPPoE change (08.01): who asked, for which customer, what changes (before and after),
/// and the Manager's decision. The router outcome is tracked separately (08.04).
/// At most one pending request per customer (enforced by a filtered unique index).
/// </summary>
public sealed class ApprovalRequest
{
    public long Id { get; set; }

    public int CustomerId { get; set; }

    public ApprovalOperation Operation { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    /// <summary>Why the change is requested. Optional, free text from the requester.</summary>
    public string? Reason { get; set; }

    /// <summary>Short summary of the state before the change, for example "Plan Fiber 50 Mbps". No personal data.</summary>
    public string? Before { get; set; }

    /// <summary>Short summary of the requested state, for example "Plan Fiber 100 Mbps". No personal data.</summary>
    public string? After { get; set; }

    /// <summary>The requested plan for <see cref="ApprovalOperation.ChangePlan"/>.</summary>
    public string? TargetPlanName { get; set; }

    public string RequestedByUserId { get; set; } = string.Empty;

    public string RequestedByUserName { get; set; } = string.Empty;

    public DateTimeOffset RequestedAtUtc { get; set; }

    public string? DecidedByUserId { get; set; }

    public string? DecidedByUserName { get; set; }

    public DateTimeOffset? DecidedAtUtc { get; set; }

    public string? DecisionNote { get; set; }

    public ProvisioningStatus ProvisioningStatus { get; set; } = ProvisioningStatus.NotApplicable;

    /// <summary>How many times sending to the router was tried. Used for safe retries in module 09.</summary>
    public int ProvisioningAttempts { get; set; }

    public DateTimeOffset? ProvisioningLastAttemptAtUtc { get; set; }

    /// <summary>Last router error, shown to Managers. Never contains passwords.</summary>
    public string? ProvisioningError { get; set; }

    public static string DisplayId(long id) => $"REQ-{id:0000}";
}
