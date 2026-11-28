using Netora.Application.Customers;
using Netora.Application.Pppoe;

namespace Netora.Application.Approvals;

public sealed record ApprovalListQuery(
    string? Status = "pending",
    string? Operation = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 10)
{
    public static readonly int[] AllowedPageSizes = [10, 20, 50];

    /// <summary>Status filter values.</summary>
    public static readonly string[] Statuses = ["pending", "approved", "rejected", "all"];
}

public sealed record ApprovalListItemDto(
    long Id,
    string DisplayId,
    int CustomerId,
    string CustomerDisplayId,
    string CustomerName,
    string PppoeUsername,
    string Operation,
    string Status,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string ProvisioningStatus);

/// <summary>Counts over every request the viewer may see (Manager-only customers excluded for Staff).</summary>
public sealed record ApprovalCountsDto(int Pending, int Approved, int Rejected);

public sealed record ApprovalListDto(
    IReadOnlyList<ApprovalListItemDto> Items,
    int TotalMatching,
    int Page,
    int PageSize,
    ApprovalCountsDto Counts);

public sealed record ApprovalDetailDto(
    long Id,
    string DisplayId,
    string Operation,
    string Status,
    string? Reason,
    string? Before,
    string? After,
    string? TargetPlanName,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionNote,
    string ProvisioningStatus,
    int ProvisioningAttempts,
    DateTimeOffset? ProvisioningLastAttemptAtUtc,
    string? ProvisioningError,
    int CustomerId,
    string CustomerDisplayId,
    string CustomerName,
    string CustomerStatus,
    string AccountState,
    string PppoeUsername,
    string PlanName,
    bool PasswordSet,
    bool IsManagerOnly,
    bool CanDecide,
    // Why Approve would be refused right now. Null when it can be approved.
    string? ApproveBlockedReason,
    // "Mock" or "RouterOS": which router the change is (or was) sent to.
    string RouterMode = "Mock",
    bool CanRetry = false,
    bool CustomerDeleted = false,
    CreatePppoeApprovalOptions? Creation = null);

public sealed record ApprovalProfileOption(int PlanId, string PlanName, string Profile, string? LocalAddress);
public sealed record ApprovalNapOption(string Code, string? Area);
public sealed record CreatePppoeApprovalOptions(int PlanId, string NapCode, string? LocalAddress, string? RemoteAddress,
    string ConcurrencyStamp, IReadOnlyList<ApprovalProfileOption> Profiles, IReadOnlyList<ApprovalNapOption> NapBoxes);

public interface IApprovalQueries
{
    Task<ApprovalListDto> ListAsync(ApprovalListQuery query, bool viewerIsManager, CancellationToken cancellationToken);

    /// <summary>Null when the request does not exist or belongs to a customer hidden from the viewer.</summary>
    Task<ApprovalDetailDto?> GetAsync(long requestId, bool viewerIsManager, CancellationToken cancellationToken);
}

public interface IApprovalCommands
{
    /// <summary>Decide by request ID (Pending Approvals page). Same rules as the PPPoE decision endpoint.</summary>
    Task<PppoeResult> DecideAsync(long requestId, DecidePppoeRequest decision, CustomerActor actor, CancellationToken cancellationToken);

    /// <summary>Manager only: sends an approved request whose router status is Not sent or Failed again (04.05).</summary>
    Task<RetryResult> RetryAsync(long requestId, CustomerActor actor, CancellationToken cancellationToken);
}

public enum RetryOutcome { Ok, Forbidden, NotFound, NotRetryable }

public sealed record RetryResult(RetryOutcome Outcome, Router.ProvisioningResult? Result = null);
