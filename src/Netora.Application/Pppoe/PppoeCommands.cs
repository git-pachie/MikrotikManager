using FluentValidation;
using Microsoft.Extensions.Logging;
using Netora.Application.Customers;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;

namespace Netora.Application.Pppoe;

/// <summary>
/// PPPoE account rules, agreed 2026-11-25:
/// <list type="bullet">
/// <item>Staff and Managers edit PPPoE details. Terminated: Managers only, and PPPoE Username and Plan stay fixed.</item>
/// <item>An Active account must be disabled first; disabling (and re-enabling) needs a Manager's approval.
/// A Manager's own request is approved by that Manager at once.</item>
/// <item>Changes are stored in the database and marked "not sent to router"; the router is updated by module 09.</item>
/// </list>
/// </summary>
internal sealed class PppoeCommands(
    IValidator<UpdatePppoeRequest> validator,
    ICustomerRepository customers,
    IApprovalRepository approvals,
    IPppoePasswordProtector passwords,
    ILogger<PppoeCommands> logger) : IPppoeCommands
{
    public async Task<PppoeResult> UpdateAsync(int customerId, UpdatePppoeRequest request, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return new(PppoeOutcome.ValidationFailed, validation.Errors.GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
        }

        var customer = await customers.FindForUpdateAsync(customerId, actor.IsManager, cancellationToken);
        if (customer is null) return new(PppoeOutcome.NotFound);
        if (customer.Status == CustomerStatus.Active) return new(PppoeOutcome.AccountActive);
        if (!customer.PppoeDetailsEditableBy(actor.IsManager)) return new(PppoeOutcome.CustomerLocked);
        if (!string.Equals(request.ConcurrencyStamp, customer.ConcurrencyStamp, StringComparison.Ordinal))
            return new(PppoeOutcome.Conflict);

        var username = request.PppoeUsername.Trim();
        var plan = request.PlanName.Trim();
        var usernameChanged = !string.Equals(username, customer.PppoeUsername, StringComparison.Ordinal);
        var planChanged = !string.Equals(plan, customer.PlanName, StringComparison.Ordinal);

        if (customer.ServiceFieldsLocked && (usernameChanged || planChanged))
        {
            var errors = new Dictionary<string, string[]>();
            const string message = "This cannot be changed for a terminated account.";
            if (usernameChanged) errors[nameof(UpdatePppoeRequest.PppoeUsername)] = [message];
            if (planChanged) errors[nameof(UpdatePppoeRequest.PlanName)] = [message];
            return new(PppoeOutcome.ServiceFieldsLocked, errors);
        }

        if (usernameChanged && await customers.PppoeUsernameExistsAsync(username, customer.Id, cancellationToken))
            return Duplicate();

        var changed = new List<string>();
        void Set<T>(string label, T current, T next, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(current, next)) return;
            assign(next);
            changed.Add(label);
        }

        Set("PPPoE Username", customer.PppoeUsername, username, v => customer.PppoeUsername = v);
        Set("Internet Plan", customer.PlanName, plan, v => customer.PlanName = v);
        Set("Remote Address", customer.PppoeRemoteAddress, Clean(request.RemoteAddress), v => customer.PppoeRemoteAddress = v);
        Set("Local Address", customer.PppoeLocalAddress, Clean(request.LocalAddress), v => customer.PppoeLocalAddress = v);
        Set("Router Comment", customer.PppoeComment, Clean(request.Comment), v => customer.PppoeComment = v);
        if (!string.IsNullOrEmpty(request.NewPassword))
        {
            // Always counted as a change: the old password is never decrypted to compare.
            customer.PppoePasswordProtected = passwords.Protect(request.NewPassword);
            changed.Add("Password");
        }

        if (changed.Count == 0) return new(PppoeOutcome.NoChanges);

        var now = DateTimeOffset.UtcNow;
        var expectedStamp = customer.ConcurrencyStamp;
        customer.UpdatedAtUtc = now;
        customer.UpdatedByUserId = actor.UserId;
        customer.RouterSyncStatus = RouterSyncStatus.NotSent;

        // Field names only. The password value is never logged or stored in history.
        var activity = Activity(customer.Id, "PppoeUpdated", "Changed: " + string.Join(", ", changed), actor, now);
        var saved = await customers.UpdateAsync(customer, expectedStamp, activity, cancellationToken);
        switch (saved)
        {
            case CustomerSaveResult.Saved:
                logger.LogInformation("PppoeUpdated CustomerId={CustomerId} ActorUserId={ActorUserId} Fields={Fields}",
                    customer.Id, actor.UserId, string.Join(",", changed));
                return new(PppoeOutcome.Ok);
            case CustomerSaveResult.DuplicatePppoeUsername:
                return Duplicate();
            default:
                return new(PppoeOutcome.Conflict);
        }
    }

    public async Task<PppoeResult> SubmitAsync(int customerId, SubmitPppoeRequest request, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ApprovalOperation>(request.Operation + "Pppoe", ignoreCase: true, out var operation)
            || !Enum.IsDefined(operation))
        {
            return new(PppoeOutcome.ValidationFailed, new Dictionary<string, string[]>
            {
                [nameof(SubmitPppoeRequest.Operation)] = ["Choose Disable or Enable."],
            });
        }

        var reason = Clean(request.Reason);
        if (reason is { Length: > 500 } || (reason?.Any(char.IsControl) ?? false))
        {
            return new(PppoeOutcome.ValidationFailed, new Dictionary<string, string[]>
            {
                [nameof(SubmitPppoeRequest.Reason)] = ["Reason must be at most 500 characters on one line."],
            });
        }

        var customer = await customers.FindForUpdateAsync(customerId, actor.IsManager, cancellationToken);
        if (customer is null) return new(PppoeOutcome.NotFound);
        if (!customer.CanBeChangedBy(actor.IsManager)) return new(PppoeOutcome.CustomerLocked);
        if (!Fits(operation, customer.Status)) return new(PppoeOutcome.NotAllowedInCurrentState);
        if (await approvals.FindPendingAsync(customer.Id, cancellationToken) is not null)
            return new(PppoeOutcome.RequestAlreadyPending);

        var now = DateTimeOffset.UtcNow;
        var approval = new ApprovalRequest
        {
            CustomerId = customer.Id,
            Operation = operation,
            Reason = reason,
            RequestedByUserId = actor.UserId,
            RequestedByUserName = actor.UserName,
            RequestedAtUtc = now,
        };
        var label = Label(operation);
        var activity = new List<CustomerActivity>
        {
            Activity(customer.Id, $"Pppoe{label}Requested", reason, actor, now, "Pending"),
        };

        Customer? changed = null;
        string? expectedStamp = null;
        if (actor.IsManager)
        {
            // A Manager's request is approved by that Manager at once.
            approval.Status = ApprovalStatus.Approved;
            approval.DecidedByUserId = actor.UserId;
            approval.DecidedByUserName = actor.UserName;
            approval.DecidedAtUtc = now;
            approval.DecisionNote = "Approved by the requesting Manager.";
            expectedStamp = customer.ConcurrencyStamp;
            Apply(customer, operation, actor, now);
            changed = customer;
            activity[0].Outcome = "Approved";
            activity.Add(Activity(customer.Id, $"Pppoe{label}Approved", "Not sent to router yet", actor, now));
        }

        if (!await approvals.AddAsync(approval, changed, expectedStamp, activity, cancellationToken))
            return new(PppoeOutcome.RequestAlreadyPending);

        logger.LogInformation("PppoeRequestSubmitted CustomerId={CustomerId} RequestId={RequestId} Operation={Operation} Status={Status} ActorUserId={ActorUserId}",
            customer.Id, approval.Id, operation, approval.Status, actor.UserId);
        return new(PppoeOutcome.Ok, Request: PppoeStates.ToDto(approval));
    }

    public async Task<PppoeResult> DecideAsync(int customerId, long requestId, DecidePppoeRequest decision, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        if (!actor.IsManager) return new(PppoeOutcome.Forbidden);

        var note = Clean(decision.Note);
        if (note is { Length: > 500 } || (note?.Any(char.IsControl) ?? false))
        {
            return new(PppoeOutcome.ValidationFailed, new Dictionary<string, string[]>
            {
                [nameof(DecidePppoeRequest.Note)] = ["Note must be at most 500 characters on one line."],
            });
        }

        var customer = await customers.FindForUpdateAsync(customerId, includeManagerOnly: true, cancellationToken);
        var approval = customer is null ? null : await approvals.FindAsync(customerId, requestId, cancellationToken);
        if (customer is null || approval is null) return new(PppoeOutcome.NotFound);
        if (approval.Status != ApprovalStatus.Pending) return new(PppoeOutcome.AlreadyDecided);

        var now = DateTimeOffset.UtcNow;
        approval.DecidedByUserId = actor.UserId;
        approval.DecidedByUserName = actor.UserName;
        approval.DecidedAtUtc = now;
        approval.DecisionNote = note;
        var label = Label(approval.Operation);

        Customer? changed = null;
        string? expectedStamp = null;
        CustomerActivity activity;
        if (decision.Approve)
        {
            // The account may have changed since the request (for example disabled by another request).
            if (!Fits(approval.Operation, customer.Status)) return new(PppoeOutcome.NotAllowedInCurrentState);
            approval.Status = ApprovalStatus.Approved;
            expectedStamp = customer.ConcurrencyStamp;
            Apply(customer, approval.Operation, actor, now);
            changed = customer;
            activity = Activity(customer.Id, $"Pppoe{label}Approved", Join("Not sent to router yet", note), actor, now);
        }
        else
        {
            approval.Status = ApprovalStatus.Rejected;
            activity = Activity(customer.Id, $"Pppoe{label}Rejected", note, actor, now, "Rejected");
        }

        if (!await approvals.SaveDecisionAsync(approval, changed, expectedStamp, activity, cancellationToken))
            return new(PppoeOutcome.AlreadyDecided);

        logger.LogInformation("PppoeRequestDecided CustomerId={CustomerId} RequestId={RequestId} Status={Status} ActorUserId={ActorUserId}",
            customer.Id, approval.Id, approval.Status, actor.UserId);
        return new(PppoeOutcome.Ok, Request: PppoeStates.ToDto(approval));
    }

    private static bool Fits(ApprovalOperation operation, CustomerStatus status) => operation switch
    {
        ApprovalOperation.DisablePppoe => status == CustomerStatus.Active,
        ApprovalOperation.EnablePppoe => status == CustomerStatus.Disabled,
        _ => false,
    };

    /// <summary>Applies an approved change to the database. The router is not touched (module 09).</summary>
    private static void Apply(Customer customer, ApprovalOperation operation, CustomerActor actor, DateTimeOffset now)
    {
        customer.Status = operation == ApprovalOperation.DisablePppoe ? CustomerStatus.Disabled : CustomerStatus.Active;
        customer.RouterSyncStatus = RouterSyncStatus.NotSent;
        customer.UpdatedAtUtc = now;
        customer.UpdatedByUserId = actor.UserId;
    }

    private static string Label(ApprovalOperation operation) => operation == ApprovalOperation.DisablePppoe ? "Disable" : "Enable";

    private static CustomerActivity Activity(int customerId, string action, string? detail, CustomerActor actor,
        DateTimeOffset at, string outcome = "Completed") => new()
    {
        CustomerId = customerId,
        Action = action,
        Detail = detail,
        ActorUserId = actor.UserId,
        ActorUserName = actor.UserName,
        Outcome = outcome,
        OccurredAtUtc = at,
    };

    private static string? Join(string first, string? second) => string.IsNullOrEmpty(second) ? first : $"{first}. {second}";

    private static PppoeResult Duplicate() => new(PppoeOutcome.DuplicatePppoeUsername, new Dictionary<string, string[]>
    {
        [nameof(UpdatePppoeRequest.PppoeUsername)] = ["This PPPoE Username is already in use."],
    });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
