using FluentValidation;
using Microsoft.Extensions.Logging;
using Netora.Application.Approvals;
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
/// <item>Module 08 adds Create (opened when a customer is added; always waits for a Manager), Change Plan (Active accounts)
/// and Terminate (Active or Disabled). A create request is approved only once a PPPoE password is set.</item>
/// </list>
/// </summary>
internal sealed class PppoeCommands(
    IValidator<UpdatePppoeRequest> validator,
    ICustomerRepository customers,
    IApprovalRepository approvals,
    IPppoePasswordProtector passwords,
    IApprovalNotifier notifier,
    Router.IRouterProvisioner provisioner,
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
        var planName = request.PlanName.Trim();
        var usernameChanged = !string.Equals(username, customer.PppoeUsername, StringComparison.Ordinal);
        var planChanged = !string.Equals(planName, customer.Plan.Name, StringComparison.Ordinal);

        if (customer.ServiceFieldsLocked && (usernameChanged || planChanged))
        {
            var errors = new Dictionary<string, string[]>();
            const string message = "This cannot be changed for a terminated account.";
            if (usernameChanged) errors[nameof(UpdatePppoeRequest.PppoeUsername)] = [message];
            if (planChanged) errors[nameof(UpdatePppoeRequest.PlanName)] = [message];
            return new(PppoeOutcome.ServiceFieldsLocked, errors);
        }

        // A new plan must be active (05); the current one is kept even if it was deactivated since.
        var plan = planChanged ? await customers.FindPlanAsync(planName, cancellationToken) : customer.Plan;
        if (plan is null || (planChanged && !plan.IsActive))
            return Invalid(nameof(UpdatePppoeRequest.PlanName), "Choose an active Internet Plan.");

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
        Set("Internet Plan", customer.PlanId, plan.Id, _ => customer.SetPlan(plan));
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
        if (!PppoeStates.TryParseOperation(request.Operation, out var operation))
            return Invalid(nameof(SubmitPppoeRequest.Operation), "Choose Create, Disable, Enable, Change Plan or Terminate.");

        var reason = Clean(request.Reason);
        if (!IsOneLine(reason, 500))
            return Invalid(nameof(SubmitPppoeRequest.Reason), "Reason must be at most 500 characters on one line.");

        Domain.Plans.InternetPlan? targetPlan = null;
        if (operation == ApprovalOperation.ChangePlan)
        {
            var targetName = Clean(request.TargetPlan);
            if (targetName is null || !IsOneLine(targetName, 100))
                return Invalid(nameof(SubmitPppoeRequest.TargetPlan), "Choose the new Internet Plan.");
            targetPlan = await customers.FindPlanAsync(targetName, cancellationToken);
            if (targetPlan is not { IsActive: true })
                return Invalid(nameof(SubmitPppoeRequest.TargetPlan), "Choose an active Internet Plan.");
        }

        var customer = await customers.FindForUpdateAsync(customerId, actor.IsManager, cancellationToken);
        if (customer is null) return new(PppoeOutcome.NotFound);
        if (!customer.CanBeChangedBy(actor.IsManager)) return new(PppoeOutcome.CustomerLocked);
        if (!PppoeStates.Fits(operation, customer.Status)) return new(PppoeOutcome.NotAllowedInCurrentState);
        if (operation == ApprovalOperation.ChangePlan && targetPlan!.Id == customer.PlanId)
            return Invalid(nameof(SubmitPppoeRequest.TargetPlan), "Choose a plan different from the current one.");
        if (await approvals.FindPendingAsync(customer.Id, cancellationToken) is not null)
            return new(PppoeOutcome.RequestAlreadyPending);

        var now = DateTimeOffset.UtcNow;
        var approval = PppoeStates.NewRequest(customer, operation, reason, targetPlan, actor, now);
        var label = Label(operation);
        var activity = new List<CustomerActivity>
        {
            Activity(customer.Id, $"Pppoe{label}Requested", reason, actor, now, "Pending"),
        };

        Customer? changed = null;
        string? expectedStamp = null;

        // A Manager's own request is approved by that Manager at once. Account creation is the exception:
        // a new customer always waits in Pending Approvals for a Manager's review (agreed 2026-11-24).
        if (actor.IsManager && operation != ApprovalOperation.CreatePppoe)
        {
            MarkApproved(approval, actor, now, "Approved by the requesting Manager.");
            expectedStamp = customer.ConcurrencyStamp;
            Apply(customer, approval, targetPlan, actor, now);
            changed = customer;
            activity[0].Outcome = "Approved";
            activity.Add(Activity(customer.Id, $"Pppoe{label}Approved", "Not sent to router yet", actor, now));
        }

        if (!await approvals.AddAsync(approval, changed, expectedStamp, activity, cancellationToken))
            return new(PppoeOutcome.RequestAlreadyPending);

        logger.LogInformation("ApprovalRequestSubmitted CustomerId={CustomerId} RequestId={RequestId} Operation={Operation} Status={Status} ActorUserId={ActorUserId}",
            customer.Id, approval.Id, operation, approval.Status, actor.UserId);
        if (approval.Status == ApprovalStatus.Pending)
            Notify(() => notifier.RequestSubmitted(ApprovalNotice.From(approval)), approval.Id);
        return new(PppoeOutcome.Ok, Request: await ProvisionIfApprovedAsync(approval, actor, cancellationToken));
    }

    public async Task<PppoeResult> DecideAsync(int customerId, long requestId, DecidePppoeRequest decision, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        if (!actor.IsManager) return new(PppoeOutcome.Forbidden);

        var note = Clean(decision.Note);
        if (!IsOneLine(note, 500))
            return Invalid(nameof(DecidePppoeRequest.Note), "Note must be at most 500 characters on one line.");

        var customer = await customers.FindForUpdateAsync(customerId, includeManagerOnly: true, cancellationToken);
        var approval = customer is null ? null : await approvals.FindAsync(customerId, requestId, cancellationToken);
        if (customer is null || approval is null) return new(PppoeOutcome.NotFound);
        if (approval.Status != ApprovalStatus.Pending) return new(PppoeOutcome.AlreadyDecided);

        var now = DateTimeOffset.UtcNow;
        var label = Label(approval.Operation);

        Customer? changed = null;
        string? expectedStamp = null;
        CustomerActivity activity;
        if (decision.Approve)
        {
            // The account may have changed since the request was made, so the rules are checked again.
            var blocked = PppoeStates.ApproveBlocker(approval, customer);
            if (blocked is not null) return new(blocked.Value);

            // A create request is approved as the account is now (details may be edited while it waits).
            if (approval.Operation == ApprovalOperation.CreatePppoe)
                approval.After = PppoeStates.CreateSummary(customer);

            // The plan chosen at request time is applied, even if it was deactivated while the request waited.
            var targetPlan = approval.TargetPlanId is { } planId ? await customers.FindPlanByIdAsync(planId, cancellationToken) : null;
            if (approval.Operation == ApprovalOperation.ChangePlan && targetPlan is null)
                return new(PppoeOutcome.NotAllowedInCurrentState);

            MarkApproved(approval, actor, now, note);
            expectedStamp = customer.ConcurrencyStamp;
            Apply(customer, approval, targetPlan, actor, now);
            changed = customer;
            activity = Activity(customer.Id, $"Pppoe{label}Approved", Join("Not sent to router yet", note), actor, now);
        }
        else
        {
            approval.Status = ApprovalStatus.Rejected;
            approval.DecidedByUserId = actor.UserId;
            approval.DecidedByUserName = actor.UserName;
            approval.DecidedAtUtc = now;
            approval.DecisionNote = note;
            approval.ProvisioningStatus = ProvisioningStatus.NotApplicable;
            activity = Activity(customer.Id, $"Pppoe{label}Rejected", note, actor, now, "Rejected");
        }

        if (!await approvals.SaveDecisionAsync(approval, changed, expectedStamp, activity, cancellationToken))
            return new(PppoeOutcome.AlreadyDecided);

        logger.LogInformation("ApprovalRequestDecided CustomerId={CustomerId} RequestId={RequestId} Operation={Operation} Status={Status} ActorUserId={ActorUserId}",
            customer.Id, approval.Id, approval.Operation, approval.Status, actor.UserId);
        Notify(() => notifier.RequestDecided(ApprovalNotice.From(approval)), approval.Id);
        return new(PppoeOutcome.Ok, Request: await ProvisionIfApprovedAsync(approval, actor, cancellationToken));
    }


    /// <summary>
    /// The decision is saved first; only then is the change sent to the router (08.04). A router failure is recorded
    /// on the request (Failed, with the error) and never undoes the approval.
    /// </summary>
    private async Task<ApprovalRequestDto> ProvisionIfApprovedAsync(ApprovalRequest approval, CustomerActor actor, CancellationToken cancellationToken)
    {
        var dto = PppoeStates.ToDto(approval);
        if (approval.Status != ApprovalStatus.Approved) return dto;
        try
        {
            var result = await provisioner.ProvisionAsync(approval.Id, actor.UserId, actor.UserName, cancellationToken);
            return dto with { ProvisioningStatus = result.Status };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "RouterProvisioningNotStarted RequestId={RequestId}", approval.Id);
            return dto;
        }
    }

    private static void MarkApproved(ApprovalRequest approval, CustomerActor actor, DateTimeOffset now, string? note)
    {
        approval.Status = ApprovalStatus.Approved;
        approval.DecidedByUserId = actor.UserId;
        approval.DecidedByUserName = actor.UserName;
        approval.DecidedAtUtc = now;
        approval.DecisionNote = note;
        // Approved is not the same as done on the router (08.04). Module 09 sends it and records the outcome.
        approval.ProvisioningStatus = ProvisioningStatus.NotSent;
    }

    /// <summary>Applies an approved change to the database. The router is not touched (module 09).</summary>
    private static void Apply(Customer customer, ApprovalRequest approval, Domain.Plans.InternetPlan? targetPlan,
        CustomerActor actor, DateTimeOffset now)
    {
        switch (approval.Operation)
        {
            case ApprovalOperation.DisablePppoe:
                customer.Status = CustomerStatus.Disabled;
                break;
            case ApprovalOperation.EnablePppoe:
            case ApprovalOperation.CreatePppoe:
                customer.Status = CustomerStatus.Active;
                break;
            case ApprovalOperation.ChangePlan:
                customer.SetPlan(targetPlan!);
                break;
            case ApprovalOperation.TerminatePppoe:
                customer.Status = CustomerStatus.Terminated;
                break;
        }
        customer.RouterSyncStatus = RouterSyncStatus.NotSent;
        customer.UpdatedAtUtc = now;
        customer.UpdatedByUserId = actor.UserId;
    }

    /// <summary>Emails are best effort: a notifier failure is logged and never changes the saved outcome (08.03).</summary>
    private void Notify(Action send, long requestId)
    {
        try
        {
            send();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ApprovalNotificationFailed RequestId={RequestId}", requestId);
        }
    }

    private static string Label(ApprovalOperation operation) => operation switch
    {
        ApprovalOperation.DisablePppoe => "Disable",
        ApprovalOperation.EnablePppoe => "Enable",
        ApprovalOperation.CreatePppoe => "Create",
        ApprovalOperation.ChangePlan => "ChangePlan",
        ApprovalOperation.TerminatePppoe => "Terminate",
        _ => operation.ToString(),
    };

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

    private static bool IsOneLine(string? value, int max) => value is null || (value.Length <= max && !value.Any(char.IsControl));

    private static PppoeResult Invalid(string field, string message) => new(PppoeOutcome.ValidationFailed,
        new Dictionary<string, string[]> { [field] = [message] });

    private static string? Join(string first, string? second) => string.IsNullOrEmpty(second) ? first : $"{first}. {second}";

    private static PppoeResult Duplicate() => new(PppoeOutcome.DuplicatePppoeUsername, new Dictionary<string, string[]>
    {
        [nameof(UpdatePppoeRequest.PppoeUsername)] = ["This PPPoE Username is already in use."],
    });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
