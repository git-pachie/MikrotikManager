using FluentValidation;
using Microsoft.Extensions.Logging;
using Netora.Domain.Customers;

namespace Netora.Application.Customers;

internal sealed class CustomerCommands(
    IValidator<CreateCustomerRequest> validator,
    ICustomerRepository repository,
    ILogger<CustomerCommands> logger) : ICustomerCommands
{
    public async Task<CreateCustomerResult> CreateAsync(CreateCustomerRequest request, string actorUserId,
        bool actorIsManager, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return new CreateCustomerResult(CreateCustomerOutcome.ValidationFailed, Errors: validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
        }

        // Only Managers may restrict visibility; Staff would otherwise hide a record from themselves and peers.
        if (request.IsManagerOnly && !actorIsManager)
            return new CreateCustomerResult(CreateCustomerOutcome.ManagerOnlyNotAllowed);

        var username = request.PppoeUsername.Trim();
        if (await repository.PppoeUsernameExistsAsync(username, cancellationToken))
            return Duplicate();

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
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CreatedByUserId = actorUserId,
        };

        if (!await repository.TryAddAsync(customer, cancellationToken))
            return Duplicate();

        // Log IDs only, never names, phone numbers or addresses (18-logging.md).
        logger.LogInformation("CustomerCreated CustomerId={CustomerId} ActorUserId={ActorUserId} Status={Status}",
            customer.Id, actorUserId, customer.Status);

        return new CreateCustomerResult(CreateCustomerOutcome.Created, customer.Id);
    }

    private static CreateCustomerResult Duplicate() => new(CreateCustomerOutcome.DuplicatePppoeUsername,
        Errors: new Dictionary<string, string[]>
        {
            [nameof(CreateCustomerRequest.PppoeUsername)] = ["This PPPoE Username is already in use."],
        });

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
