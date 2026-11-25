using Netora.Domain.Approvals;
using Netora.Domain.Customers;

namespace Netora.Application.Approvals;

/// <summary>What an approval email is about. Customer personal data is deliberately left out.</summary>
public sealed record ApprovalNotice(
    long RequestId,
    string RequestDisplayId,
    ApprovalOperation Operation,
    ApprovalStatus Status,
    int CustomerId,
    string CustomerDisplayId,
    string RequestedByUserId,
    string RequestedByUserName,
    string? DecidedByUserName,
    string? DecisionNote)
{
    public static ApprovalNotice From(ApprovalRequest r) => new(
        r.Id, ApprovalRequest.DisplayId(r.Id), r.Operation, r.Status, r.CustomerId, Customer.DisplayId(r.CustomerId),
        r.RequestedByUserId, r.RequestedByUserName, r.DecidedByUserName, r.DecisionNote);
}

/// <summary>
/// Approval emails (08.03): Managers on a new request, the requester on a decision.
/// Implementations queue the message and return at once. They must never throw, and a failed email
/// never changes the approval outcome.
/// </summary>
public interface IApprovalNotifier
{
    void RequestSubmitted(ApprovalNotice notice);

    void RequestDecided(ApprovalNotice notice);
}
