using FluentValidation;
using Microsoft.Extensions.Logging;
using Netora.Domain.Customers;

namespace Netora.Application.Customers;

internal sealed class CustomerCommands(
    IValidator<CreateCustomerRequest> createValidator,
    IValidator<UpdateCustomerRequest> updateValidator,
    ICustomerRepository repository,
    ILogger<CustomerCommands> logger) : ICustomerCommands
{
    public async Task<CreateCustomerResult> CreateAsync(CreateCustomerRequest request, CustomerActor actor,
        CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
            return new CreateCustomerResult(CreateCustomerOutcome.ValidationFailed, Errors: ToErrors(validation));

        // Only Managers may restrict visibility; Staff would otherwise hide a record from themselves and peers.
        if (request.IsManagerOnly && !actor.IsManager)
            return new CreateCustomerResult(CreateCustomerOutcome.ManagerOnlyNotAllowed);

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
            PlanName = request.PlanName.Trim(),
            NapBox = Clean(request.NapBox) ?? Customer.UnknownNap,
            OnuSerial = Clean(request.OnuSerial),
            OnuModel = Clean(request.OnuModel),
            // Agreed rule: every new customer waits for a Manager's approval. No router call happens here.
            Status = CustomerStatus.PendingApproval,
            IsManagerOnly = request.IsManagerOnly,
            IsSampleData = false,
            CreatedAtUtc = now,
            CreatedByUserId = actor.UserId,
        };

        var activity = NewActivity(actor, "CustomerCreated", "Submitted for approval", now);
        if (await repository.AddAsync(customer, activity, cancellationToken) != CustomerSaveResult.Saved)
            return DuplicateOnCreate();

        // Log IDs only, never names, phone numbers or addresses (18-logging.md).
        logger.LogInformation("CustomerCreated CustomerId={CustomerId} ActorUserId={ActorUserId} Status={Status}",
            customer.Id, actor.UserId, customer.Status);
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
        var plan = request.PlanName.Trim();
        var usernameChanged = !string.Equals(username, customer.PppoeUsername, StringComparison.Ordinal);
        var planChanged = !string.Equals(plan, customer.PlanName, StringComparison.Ordinal);

        if (customer.ServiceFieldsLocked && (usernameChanged || planChanged))
        {
            var errors = new Dictionary<string, string[]>();
            const string message = "This can only be changed through a Manager-approved request.";
            if (usernameChanged) errors[nameof(UpdateCustomerRequest.PppoeUsername)] = [message];
            if (planChanged) errors[nameof(UpdateCustomerRequest.PlanName)] = [message];
            return new UpdateCustomerResult(UpdateCustomerOutcome.ServiceFieldsLocked, errors);
        }

        if (usernameChanged && await repository.PppoeUsernameExistsAsync(username, customer.Id, cancellationToken))
            return DuplicateOnUpdate();

        var changed = ApplyChanges(customer, request, username, plan);
        if (changed.Count == 0)
            return new UpdateCustomerResult(UpdateCustomerOutcome.NoChanges);

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
    private static List<string> ApplyChanges(Customer c, UpdateCustomerRequest r, string username, string plan)
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
        Set("Internet Plan", c.PlanName, plan, v => c.PlanName = v);
        Set("NAP Box", c.NapBox, Clean(r.NapBox) ?? Customer.UnknownNap, v => c.NapBox = v);
        Set("ONU Serial Number", c.OnuSerial, Clean(r.OnuSerial), v => c.OnuSerial = v);
        Set("ONU Brand / Model", c.OnuModel, Clean(r.OnuModel), v => c.OnuModel = v);
        Set("Visibility", c.IsManagerOnly, r.IsManagerOnly, v => c.IsManagerOnly = v);
        return changed;
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
