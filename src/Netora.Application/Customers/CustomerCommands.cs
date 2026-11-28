using FluentValidation;
using Microsoft.Extensions.Logging;
using Netora.Application.Approvals;
using Netora.Application.Pppoe;
using Netora.Application.Router;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Domain.Network;
using Netora.Domain.Plans;

namespace Netora.Application.Customers;

internal sealed class CustomerCommands(
    IValidator<CreateCustomerRequest> createValidator,
    IValidator<UpdateCustomerRequest> updateValidator,
    ICustomerRepository repository,
    IApprovalNotifier notifier,
    IRouterClient router,
    ILogger<CustomerCommands> logger) : ICustomerCommands
{
    public async Task<DeleteCustomerOutcome> DeleteAsync(int id, DeleteCustomerRequest request, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        if (!actor.IsManager) return DeleteCustomerOutcome.Forbidden;
        var customer = await repository.FindForUpdateAsync(id, includeManagerOnly: true, cancellationToken);
        if (customer is null) return DeleteCustomerOutcome.NotFound;
        if (customer.PppoeDeletedAtUtc is null) return DeleteCustomerOutcome.PppoeDeletionRequired;
        if (!string.Equals(customer.ConcurrencyStamp, request.ConcurrencyStamp, StringComparison.Ordinal))
            return DeleteCustomerOutcome.Conflict;

        try
        {
            var marker = RouterRules.Marker(id);
            var secrets = await router.ListSecretsAsync(cancellationToken, includePasswords: false);
            if (secrets.Any(s => string.Equals(s.Name, customer.PppoeUsername, StringComparison.OrdinalIgnoreCase)
                || s.Comment?.Contains(marker, StringComparison.Ordinal) == true))
                return DeleteCustomerOutcome.PppoeExistsOnRouter;
        }
        catch (RouterException ex)
        {
            logger.LogWarning("CustomerDeletionRouterCheckFailed CustomerId={CustomerId} Kind={Kind}", id, ex.Kind);
            return DeleteCustomerOutcome.RouterUnavailable;
        }

        var now = DateTimeOffset.UtcNow;
        customer.DeletedAtUtc = now;
        customer.UpdatedAtUtc = now;
        customer.UpdatedByUserId = actor.UserId;
        var activity = NewActivity(actor, "CustomerDeleted", null, now, id);
        return await repository.UpdateAsync(customer, request.ConcurrencyStamp, activity, cancellationToken) == CustomerSaveResult.Saved
            ? DeleteCustomerOutcome.Deleted
            : DeleteCustomerOutcome.Conflict;
    }

    public async Task<CreateCustomerResult> CreateAsync(CreateCustomerRequest request, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return new CreateCustomerResult(CreateCustomerOutcome.ValidationFailed, Errors: ToErrors(validation));

        // Only Managers may restrict visibility; Staff would otherwise hide a record from themselves and peers.
        if (request.IsManagerOnly && !actor.IsManager)
            return new CreateCustomerResult(CreateCustomerOutcome.ManagerOnlyNotAllowed);

        // New customers get active plans and NAP boxes only (05, 06).
        var plan = await repository.FindPlanAsync(request.PlanName.Trim(), cancellationToken);
        var nap = await repository.FindNapAsync(Clean(request.NapBox) ?? Customer.UnknownNap, cancellationToken);
        var catalogErrors = CatalogErrors(plan is { IsActive: true }, nap is { IsActive: true });
        if (catalogErrors is not null)
            return new CreateCustomerResult(CreateCustomerOutcome.ValidationFailed, Errors: catalogErrors);

        var username = request.PppoeUsername.Trim();
        if (await repository.PppoeUsernameExistsAsync(username, null, cancellationToken))
            return DuplicateOnCreate();

        var now = DateTimeOffset.UtcNow;
        var customer = new Customer
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            MobileNumber = Clean(request.MobileNumber),
            Email = Clean(request.Email),
            Address = request.Address.Trim(),
            Notes = Clean(request.Notes),
            PppoeUsername = username,
            OnuSerial = Clean(request.OnuSerial),
            OnuModel = Clean(request.OnuModel),
            // Agreed rule: every new customer waits for a Manager's approval. No router call happens here.
            Status = CustomerStatus.PendingApproval,
            IsManagerOnly = request.IsManagerOnly,
            IsSampleData = false,
            CreatedAtUtc = now,
            CreatedByUserId = actor.UserId,
        };
        customer.SetPlan(plan!);
        customer.SetNap(nap!);

        var activity = NewActivity(actor, "CustomerCreated", "Submitted for approval", now);

        // The PPPoE account is created only after a Manager approves this request (08.01). It always waits,
        // also when a Manager adds the customer.
        var creationRequest = PppoeStates.NewRequest(customer, ApprovalOperation.CreatePppoe, null, null, actor, now);
        if (await repository.AddAsync(customer, activity, creationRequest, cancellationToken) != CustomerSaveResult.Saved)
            return DuplicateOnCreate();

        // Log IDs only, never names, phone numbers or addresses (18-logging.md).
        logger.LogInformation("CustomerCreated CustomerId={CustomerId} RequestId={RequestId} ActorUserId={ActorUserId} Status={Status}",
            customer.Id, creationRequest.Id, actor.UserId, customer.Status);
        try
        {
            notifier.RequestSubmitted(ApprovalNotice.From(creationRequest));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ApprovalNotificationFailed RequestId={RequestId}", creationRequest.Id);
        }
        return new CreateCustomerResult(CreateCustomerOutcome.Created, customer.Id);
    }

    public async Task<UpdateCustomerResult> UpdateAsync(int id, UpdateCustomerRequest request, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return new UpdateCustomerResult(UpdateCustomerOutcome.ValidationFailed, ToErrors(validation));

        // Hidden and missing look identical, so Staff cannot probe for Manager-only customers.
        var customer = await repository.FindForUpdateAsync(id, actor.IsManager, cancellationToken);
        if (customer is null)
            return new UpdateCustomerResult(UpdateCustomerOutcome.NotFound);

        // Terminated: Staff may only view; Managers may still edit details (agreed 2026-11-24).
        if (!customer.CanBeChangedBy(actor.IsManager))
            return new UpdateCustomerResult(UpdateCustomerOutcome.Locked);

        if (request.IsManagerOnly != customer.IsManagerOnly && !actor.IsManager)
            return new UpdateCustomerResult(UpdateCustomerOutcome.ManagerOnlyNotAllowed);

        if (!string.Equals(request.ConcurrencyStamp, customer.ConcurrencyStamp, StringComparison.Ordinal))
            return new UpdateCustomerResult(UpdateCustomerOutcome.Conflict);

        var username = request.PppoeUsername.Trim();
        var planName = request.PlanName.Trim();
        var napCode = Clean(request.NapBox) ?? Customer.UnknownNap;
        var usernameChanged = !string.Equals(username, customer.PppoeUsername, StringComparison.Ordinal);
        var planChanged = !string.Equals(planName, customer.Plan.Name, StringComparison.Ordinal);
        var napChanged = !string.Equals(napCode, customer.Nap.Code, StringComparison.Ordinal);

        if (customer.ServiceFieldsLocked && (usernameChanged || planChanged))
        {
            var errors = new Dictionary<string, string[]>();
            const string message = "This can only be changed through a Manager-approved request.";
            if (usernameChanged) errors[nameof(UpdateCustomerRequest.PppoeUsername)] = [message];
            if (planChanged) errors[nameof(UpdateCustomerRequest.PlanName)] = [message];
            return new UpdateCustomerResult(UpdateCustomerOutcome.ServiceFieldsLocked, errors);
        }

        // A customer keeps an inactive plan or NAP it already has; a new choice must be active.
        var plan = planChanged ? await repository.FindPlanAsync(planName, cancellationToken) : customer.Plan;
        var nap = napChanged ? await repository.FindNapAsync(napCode, cancellationToken) : customer.Nap;
        var catalogErrors = CatalogErrors(!planChanged || plan is { IsActive: true }, !napChanged || nap is { IsActive: true });
        if (catalogErrors is not null)
            return new UpdateCustomerResult(UpdateCustomerOutcome.ValidationFailed, catalogErrors);

        if (usernameChanged && await repository.PppoeUsernameExistsAsync(username, customer.Id, cancellationToken))
            return DuplicateOnUpdate();

        var changed = ApplyChanges(customer, request, username, plan!, nap!);
        if (changed.Count == 0)
            return new UpdateCustomerResult(UpdateCustomerOutcome.NoChanges);

        return await SaveAsync(customer, changed, actor, cancellationToken);
    }

    public async Task<UpdateCustomerResult> MoveNapAsync(int id, MoveNapRequest request, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        var code = Clean(request.NapBox);
        if (code is null || code.Length > 50)
            return new UpdateCustomerResult(UpdateCustomerOutcome.ValidationFailed, new Dictionary<string, string[]>
            {
                [nameof(MoveNapRequest.NapBox)] = ["Choose a NAP box."],
            });

        var customer = await repository.FindForUpdateAsync(id, actor.IsManager, cancellationToken);
        if (customer is null) return new UpdateCustomerResult(UpdateCustomerOutcome.NotFound);
        if (!customer.CanBeChangedBy(actor.IsManager)) return new UpdateCustomerResult(UpdateCustomerOutcome.Locked);
        if (!string.Equals(request.ConcurrencyStamp, customer.ConcurrencyStamp, StringComparison.Ordinal))
            return new UpdateCustomerResult(UpdateCustomerOutcome.Conflict);
        if (string.Equals(code, customer.Nap.Code, StringComparison.Ordinal))
            return new UpdateCustomerResult(UpdateCustomerOutcome.NoChanges);

        var nap = await repository.FindNapAsync(code, cancellationToken);
        if (CatalogErrors(true, nap is { IsActive: true }) is { } errors)
            return new UpdateCustomerResult(UpdateCustomerOutcome.ValidationFailed, errors);

        customer.SetNap(nap!);
        return await SaveAsync(customer, ["NAP Box"], actor, cancellationToken);
    }

    private async Task<UpdateCustomerResult> SaveAsync(Customer customer, List<string> changed, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expectedStamp = customer.ConcurrencyStamp;
        customer.UpdatedAtUtc = now;
        customer.UpdatedByUserId = actor.UserId;

        // Field names only, never the old or new values (18-logging.md).
        var activity = NewActivity(actor, "CustomerUpdated", "Changed: " + string.Join(", ", changed), now, customer.Id);
        var saved = await repository.UpdateAsync(customer, expectedStamp, activity, cancellationToken);

        switch (saved)
        {
            case CustomerSaveResult.Saved:
                logger.LogInformation("CustomerUpdated CustomerId={CustomerId} ActorUserId={ActorUserId} Fields={Fields}",
                    customer.Id, actor.UserId, string.Join(",", changed));
                return new UpdateCustomerResult(UpdateCustomerOutcome.Updated);
            case CustomerSaveResult.DuplicatePppoeUsername:
                return DuplicateOnUpdate();
            default:
                return new UpdateCustomerResult(UpdateCustomerOutcome.Conflict);
        }
    }

    /// <summary>Copies the edited values onto the customer and returns the display names of the fields that changed.</summary>
    private static List<string> ApplyChanges(Customer c, UpdateCustomerRequest r, string username, InternetPlan plan, NapBox nap)
    {
        var changed = new List<string>();

        void Set<T>(string label, T current, T next, Action<T> assign)
        {
            if (EqualityComparer<T>.Default.Equals(current, next)) return;
            assign(next);
            changed.Add(label);
        }

        Set("First Name", c.FirstName, r.FirstName.Trim(), v => c.FirstName = v);
        Set("Last Name", c.LastName, r.LastName.Trim(), v => c.LastName = v);
        Set("Mobile Number", c.MobileNumber, Clean(r.MobileNumber), v => c.MobileNumber = v);
        Set("Email", c.Email, Clean(r.Email), v => c.Email = v);
        Set("Installation Address", c.Address, r.Address.Trim(), v => c.Address = v);
        Set("Notes", c.Notes, Clean(r.Notes), v => c.Notes = v);
        Set("PPPoE Username", c.PppoeUsername, username, v => c.PppoeUsername = v);
        Set("Internet Plan", c.PlanId, plan.Id, _ => c.SetPlan(plan));
        Set("NAP Box", c.NapBoxId, nap.Id, _ => c.SetNap(nap));
        Set("ONU Serial Number", c.OnuSerial, Clean(r.OnuSerial), v => c.OnuSerial = v);
        Set("ONU Brand / Model", c.OnuModel, Clean(r.OnuModel), v => c.OnuModel = v);
        Set("Visibility", c.IsManagerOnly, r.IsManagerOnly, v => c.IsManagerOnly = v);
        return changed;
    }

    /// <summary>Field errors for a plan or NAP that does not exist or is inactive; null when both are fine.</summary>
    internal static Dictionary<string, string[]>? CatalogErrors(bool planOk, bool napOk)
    {
        if (planOk && napOk) return null;
        var errors = new Dictionary<string, string[]>();
        if (!planOk) errors[nameof(CreateCustomerRequest.PlanName)] = ["Choose an active Internet Plan."];
        if (!napOk) errors[nameof(CreateCustomerRequest.NapBox)] = ["Choose an active NAP box."];
        return errors;
    }

    private static CustomerActivity NewActivity(CustomerActor actor, string action, string? detail,
        DateTimeOffset at, int customerId = 0) => new()
    {
        CustomerId = customerId,
        Action = action,
        Detail = detail,
        ActorUserId = actor.UserId,
        ActorUserName = actor.UserName,
        Outcome = "Completed",
        OccurredAtUtc = at,
    };

    private static Dictionary<string, string[]> ToErrors(FluentValidation.Results.ValidationResult validation)
        => validation.Errors.GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

    private static CreateCustomerResult DuplicateOnCreate() => new(CreateCustomerOutcome.DuplicatePppoeUsername,
        Errors: DuplicateErrors());

    private static UpdateCustomerResult DuplicateOnUpdate() => new(UpdateCustomerOutcome.DuplicatePppoeUsername,
        DuplicateErrors());

    private static Dictionary<string, string[]> DuplicateErrors() => new()
    {
        [nameof(CreateCustomerRequest.PppoeUsername)] = ["This PPPoE Username is already in use."],
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
