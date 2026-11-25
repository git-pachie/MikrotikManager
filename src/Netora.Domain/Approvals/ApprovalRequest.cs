namespace Netora.Domain.Approvals;

/// <summary>Operations that need a Manager's approval. Module 08 adds the rest (create, plan change, terminate).</summary>
public enum ApprovalOperation
{
    DisablePppoe = 1,
    EnablePppoe = 2,
}

public enum ApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
}

/// <summary>
/// A request for a sensitive PPPoE change (08.01): who asked, for which customer, what, and the Manager's decision.
/// The approval decision is stored here; the router outcome is tracked separately on the customer (08.04).
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

    public string RequestedByUserId { get; set; } = string.Empty;

    public string RequestedByUserName { get; set; } = string.Empty;

    public DateTimeOffset RequestedAtUtc { get; set; }

    public string? DecidedByUserId { get; set; }

    public string? DecidedByUserName { get; set; }

    public DateTimeOffset? DecidedAtUtc { get; set; }

    public string? DecisionNote { get; set; }
}
