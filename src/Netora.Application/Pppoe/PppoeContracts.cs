using System.Net;
using System.Net.Sockets;
using FluentValidation;
using Netora.Application.Customers;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;

namespace Netora.Application.Pppoe;

public sealed record PppoeListQuery(
    string? Search = null,
    string? State = null,
    int Page = 1,
    int PageSize = 10,
    string? SortBy = null,
    string? SortDir = null,
    string? Session = null)
{
    public static readonly int[] AllowedPageSizes = [10, 20, 50];
    public static readonly string[] SortColumns = ["username", "customer", "plan", "state", "requested"];

    /// <summary>Filter values. "requested" = has a pending approval request.</summary>
    public static readonly string[] States = ["enabled", "disabled", "notcreated", "terminated", "requested"];

    public bool Descending => string.Equals(SortDir, "desc", StringComparison.OrdinalIgnoreCase);

    public static string? NormalizeSession(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "online" or "offline" ? normalized : null;
    }
}

public sealed record PppoeListItemDto(
    int CustomerId,
    string DisplayId,
    string CustomerName,
    string PppoeUsername,
    string PlanName,
    string AccountState,
    string RouterSyncStatus,
    bool PasswordSet,
    string? PendingOperation,
    bool CanDelete = false,
    bool? SessionConnected = null);

public sealed record PppoeStatsDto(int Total, int Enabled, int Disabled, int NotCreated, int PendingRequests);

public sealed record PppoeListDto(IReadOnlyList<PppoeListItemDto> Items, int TotalMatching, int Page, int PageSize, PppoeStatsDto Stats,
    string? SessionRouterMode = null, bool SessionFilterUnavailable = false);

public sealed record ApprovalRequestDto(
    long Id,
    string Operation,
    string Status,
    string? Reason,
    string RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string? DecidedBy,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionNote,
    string? Before = null,
    string? After = null,
    string ProvisioningStatus = "NotApplicable");

/// <summary>Everything the Edit PPPoE page needs. The password itself is never included, only whether one is set.</summary>
public sealed record PppoeDetailDto(
    int CustomerId,
    string DisplayId,
    string CustomerName,
    string CustomerStatus,
    string AccountState,
    string PppoeUsername,
    string PlanName,
    bool PasswordSet,
    string? RemoteAddress,
    string? LocalAddress,
    string? Comment,
    string RouterSyncStatus,
    string ConcurrencyStamp,
    bool CanEdit,
    bool ServiceFieldsLocked,
    bool CanRequestDisable,
    bool CanRequestEnable,
    ApprovalRequestDto? PendingRequest,
    IReadOnlyList<ApprovalRequestDto> RecentRequests,
    bool CanRequestPlanChange = false,
    bool CanRequestTerminate = false,
    bool CanResubmitCreate = false,
    bool CanDelete = false,
    long? DeletionRequestId = null,
    PppoeSessionDto? Session = null);

public sealed record PppoeSessionDto(bool? Connected, string RouterMode, string? DisconnectReference = null);
public sealed record DisconnectPppoeRequest(string? SessionReference);
public enum DisconnectPppoeOutcome { Disconnected, SessionChanged, NotFound, Forbidden, RouterError }
public sealed record DisconnectPppoeResult(DisconnectPppoeOutcome Outcome, string? RouterMode = null);

public interface IPppoeSessionCommands
{
    Task<DisconnectPppoeResult> DisconnectAsync(int customerId, DisconnectPppoeRequest request, CustomerActor actor, CancellationToken ct);
}

public sealed record DeletePppoeRequest(string ConcurrencyStamp);

/// <summary>Edit PPPoE form. Leave <see cref="NewPassword"/> empty to keep the current password.</summary>
public sealed record UpdatePppoeRequest(
    string PppoeUsername,
    string PlanName,
    string? NewPassword,
    string? RemoteAddress,
    string? LocalAddress,
    string? Comment,
    string ConcurrencyStamp);

/// <param name="Operation">Create, Disable, Enable, ChangePlan or Terminate.</param>
/// <param name="TargetPlan">The new Internet Plan, required for ChangePlan.</param>
public sealed record SubmitPppoeRequest(string Operation, string? Reason, string? TargetPlan = null);

public sealed record DecidePppoeRequest(bool Approve, string? Note, CreatePppoeApprovalInput? Creation = null);

public sealed record CreatePppoeApprovalInput(int PlanId, string NapCode, string? NewPassword,
    string? LocalAddress, string? RemoteAddress, string? ConcurrencyStamp)
{
    public const string DefaultPassword = "12345678";
}

public static class PppoePasswordMask
{
    public static bool IsMasked(string? value) => !string.IsNullOrEmpty(value) && value.All(c => c == '*');
}

public sealed class UpdatePppoeRequestValidator : AbstractValidator<UpdatePppoeRequest>
{
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 64;

    public UpdatePppoeRequestValidator()
    {
        RuleFor(x => x.PppoeUsername).NotEmpty().WithName("PPPoE Username")
            .Matches(CustomerFieldsValidator<CreateCustomerRequest>.PppoeUsernamePattern)
            .WithMessage("PPPoE Username must be 3 to 40 characters: letters, digits, '.', '_' or '-'.");
        RuleFor(x => x.PlanName).NotEmpty().WithName("Internet Plan").MaximumLength(100);
        RuleFor(x => x.NewPassword!)
            .Cascade(CascadeMode.Stop)
            .Must(p => !PppoePasswordMask.IsMasked(p)).WithMessage("Enter the actual PPPoE password instead of asterisks.")
            .Length(PasswordMinLength, PasswordMaxLength).WithName("Password")
            .Must(p => p.All(IsPasswordCharacter))
            .WithMessage("Password may use letters, digits and symbols only (no spaces or special characters).")
            .When(x => !string.IsNullOrEmpty(x.NewPassword));
        RuleFor(x => x.RemoteAddress).Must(BeIPv4OrEmpty).WithName("Remote Address")
            .WithMessage("Enter an IPv4 address such as 10.0.0.25, or leave it empty.");
        RuleFor(x => x.LocalAddress).Must(BeIPv4OrEmpty).WithName("Local Address")
            .WithMessage("Enter an IPv4 address such as 10.0.0.1, or leave it empty.");
        RuleFor(x => x.Comment).MaximumLength(200)
            .Must(c => c is null || !c.Any(char.IsControl)).WithMessage("Comment cannot contain line breaks.");
        RuleFor(x => x.ConcurrencyStamp).NotEmpty().MaximumLength(32);
    }

    internal static bool IsPasswordCharacter(char c) => c > ' ' && c < 127;

    public static bool BeIPv4OrEmpty(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var v = value.Trim();
        return v.Count(c => c == '.') == 3
               && IPAddress.TryParse(v, out var ip)
               && ip.AddressFamily == AddressFamily.InterNetwork
               && ip.ToString() == v;   // rejects forms like "010.1.1.1" that parse differently
    }
}

public enum PppoeOutcome
{
    Ok,
    NoChanges,
    ValidationFailed,
    NotFound,

    /// <summary>The account is Active: it must be disabled (Manager-approved) before its details can change.</summary>
    AccountActive,

    /// <summary>Terminated and the caller is Staff.</summary>
    CustomerLocked,

    /// <summary>Terminated: PPPoE Username and Internet Plan stay fixed.</summary>
    ServiceFieldsLocked,
    DuplicatePppoeUsername,
    Conflict,

    /// <summary>The request does not fit the account state (for example disabling an account that is not Active).</summary>
    NotAllowedInCurrentState,

    /// <summary>A request is already waiting for this customer.</summary>
    RequestAlreadyPending,

    /// <summary>Only Managers decide requests.</summary>
    Forbidden,

    /// <summary>The request was already approved or rejected.</summary>
    AlreadyDecided,

    DeletionInProgress,
    PasswordUnavailable,
}

public sealed record PppoeResult(PppoeOutcome Outcome, IReadOnlyDictionary<string, string[]>? Errors = null,
    ApprovalRequestDto? Request = null);

public sealed class PppoePasswordDto(string password)
{
    public string Password { get; } = password;
}
public sealed record PppoePasswordResult(PppoeOutcome Outcome, PppoePasswordDto? Value = null);

public interface IPppoeQueries
{
    Task<PppoeListDto> ListAsync(PppoeListQuery query, bool viewerIsManager, CancellationToken cancellationToken);

    /// <summary>Null when the customer does not exist or is hidden from the viewer.</summary>
    Task<PppoeDetailDto?> GetAsync(int customerId, bool viewerIsManager, CancellationToken cancellationToken);
}

public interface IPppoeCommands
{
    Task<PppoePasswordResult> RevealPasswordAsync(int customerId, CustomerActor actor, CancellationToken cancellationToken);

    Task<PppoeResult> DeleteAsync(int customerId, DeletePppoeRequest request, CustomerActor actor, CancellationToken cancellationToken);

    Task<PppoeResult> UpdateAsync(int customerId, UpdatePppoeRequest request, CustomerActor actor, CancellationToken cancellationToken);

    /// <summary>
    /// Staff: creates a pending request for a Manager. Manager: the request is approved by that Manager at once and
    /// applied to the database. The router is updated later (module 09).
    /// </summary>
    Task<PppoeResult> SubmitAsync(int customerId, SubmitPppoeRequest request, CustomerActor actor, CancellationToken cancellationToken);

    Task<PppoeResult> DecideAsync(int customerId, long requestId, DecidePppoeRequest decision, CustomerActor actor,
        CancellationToken cancellationToken);
}

/// <summary>Encrypts PPPoE passwords for storage. Implemented with ASP.NET Core Data Protection.</summary>
public interface IPppoePasswordProtector
{
    string Protect(string password);

    /// <summary>Decrypts the stored PPPoE password for server-side use.</summary>
    string Unprotect(string protectedPassword);
}

public interface IApprovalRepository
{
    Task<ApprovalRequest?> FindPendingAsync(int customerId, CancellationToken cancellationToken);

    Task<ApprovalRequest?> FindAsync(int customerId, long requestId, CancellationToken cancellationToken);

    Task<ApprovalRequest?> FindByIdAsync(long requestId, CancellationToken cancellationToken);

    /// <summary>
    /// Saves a new request, its activity lines and, when the request is approved at once, the changed customer.
    /// Returns false when another pending request was created at the same moment or the customer changed meanwhile.
    /// </summary>
    Task<bool> AddAsync(ApprovalRequest request, Customer? changedCustomer, string? expectedCustomerStamp,
        IEnumerable<CustomerActivity> activity, CancellationToken cancellationToken);

    /// <summary>Saves a decision. Returns false if someone else decided first or the customer changed meanwhile.</summary>
    Task<bool> SaveDecisionAsync(ApprovalRequest request, Customer? changedCustomer, string? expectedCustomerStamp,
        CustomerActivity activity, CancellationToken cancellationToken);
}

public static class PppoeStates
{
    public static string AccountState(CustomerStatus status) => status switch
    {
        CustomerStatus.Active => "Enabled",
        CustomerStatus.Disabled => "Disabled",
        CustomerStatus.Terminated => "Terminated",
        _ => "NotCreated",
    };

    public static ApprovalRequestDto ToDto(ApprovalRequest r) => new(r.Id, r.Operation.ToString(), r.Status.ToString(),
        r.Reason, r.RequestedByUserName, r.RequestedAtUtc, r.DecidedByUserName, r.DecidedAtUtc, r.DecisionNote,
        r.Before, r.After, r.ProvisioningStatus.ToString());

    /// <summary>Operation names accepted by the API, mapped to the stored operation.</summary>
    public static readonly IReadOnlyDictionary<string, ApprovalOperation> OperationNames =
        new Dictionary<string, ApprovalOperation>(StringComparer.OrdinalIgnoreCase)
        {
            ["Create"] = ApprovalOperation.CreatePppoe,
            ["Disable"] = ApprovalOperation.DisablePppoe,
            ["Enable"] = ApprovalOperation.EnablePppoe,
            ["ChangePlan"] = ApprovalOperation.ChangePlan,
            ["Terminate"] = ApprovalOperation.TerminatePppoe,
        };

    public static bool TryParseOperation(string? value, out ApprovalOperation operation)
    {
        operation = default;
        return value is not null && OperationNames.TryGetValue(value.Trim(), out operation);
    }

    /// <summary>Account states a request applies to (checked when it is submitted and again when it is approved).</summary>
    public static bool Fits(ApprovalOperation operation, CustomerStatus status) => operation switch
    {
        ApprovalOperation.CreatePppoe => status == CustomerStatus.PendingApproval,
        ApprovalOperation.DisablePppoe => status == CustomerStatus.Active,
        ApprovalOperation.EnablePppoe => status == CustomerStatus.Disabled,
        // Disabled accounts can be edited directly, so a plan change request is only needed while Active.
        ApprovalOperation.ChangePlan => status == CustomerStatus.Active,
        ApprovalOperation.TerminatePppoe => status is CustomerStatus.Active or CustomerStatus.Disabled,
        _ => false,
    };

    /// <summary>Why approving would be refused now, or null. Used when deciding and by the Approval Details page.</summary>
    public static PppoeOutcome? ApproveBlocker(ApprovalRequest approval, Customer customer)
    {
        if (customer.PppoeDeletionRequestedAtUtc is not null) return PppoeOutcome.DeletionInProgress;
        if (!Fits(approval.Operation, customer.Status)) return PppoeOutcome.NotAllowedInCurrentState;
        if (approval.Operation == ApprovalOperation.ChangePlan
            && (approval.TargetPlanId is null || approval.TargetPlanId == customer.PlanId))
            return PppoeOutcome.NotAllowedInCurrentState;
        return null;
    }

    /// <summary>The "after" summary of a create request: what the router account will be.</summary>
    public static string CreateSummary(Customer c) => $"Enabled, username {c.PppoeUsername}, plan {c.Plan.Name}";

    /// <summary>New request with its before and after summaries. Status is Pending.</summary>
    public static ApprovalRequest NewRequest(Customer customer, ApprovalOperation operation, string? reason,
        Domain.Plans.InternetPlan? targetPlan, CustomerActor actor, DateTimeOffset now)
    {
        var state = AccountState(customer.Status);
        var (before, after) = operation switch
        {
            ApprovalOperation.CreatePppoe => ("Not created", CreateSummary(customer)),
            ApprovalOperation.DisablePppoe => (state, "Disabled"),
            ApprovalOperation.EnablePppoe => (state, "Enabled"),
            ApprovalOperation.ChangePlan => ($"Plan {customer.Plan.Name}", $"Plan {targetPlan?.Name}"),
            ApprovalOperation.TerminatePppoe => (state, "Terminated"),
            ApprovalOperation.DeletePppoe => (state, "PPPoE account deleted"),
            _ => (state, state),
        };
        return new ApprovalRequest
        {
            CustomerId = customer.Id,
            Operation = operation,
            Reason = reason,
            Before = before,
            After = after,
            TargetPlanId = targetPlan?.Id,
            TargetPlanName = targetPlan?.Name,
            RequestedByUserId = actor.UserId,
            RequestedByUserName = actor.UserName,
            RequestedAtUtc = now,
            ProvisioningStatus = ProvisioningStatus.NotApplicable,
        };
    }
}
