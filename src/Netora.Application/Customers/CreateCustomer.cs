using FluentValidation;

namespace Netora.Application.Customers;

/// <summary>Fields of the approved Add/Edit Customer form.</summary>
public sealed record CreateCustomerRequest(
    string FirstName,
    string LastName,
    string? MobileNumber,
    string? Email,
    string Address,
    string? Notes,
    string PppoeUsername,
    string PlanName,
    string? NapBox,
    string? OnuSerial,
    string? OnuModel,
    bool IsManagerOnly);

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public const string PppoeUsernamePattern = "^[A-Za-z0-9._-]{3,40}$";
    public const string MobilePattern = @"^[0-9+()\- ]{7,30}$";

    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MobileNumber).Matches(MobilePattern)
            .WithMessage("Mobile Number may contain digits, spaces, +, -, ( and ), 7 to 30 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.MobileNumber));
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Address).NotEmpty().WithName("Installation Address").MaximumLength(300);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.PppoeUsername).NotEmpty().WithName("PPPoE Username")
            .Matches(PppoeUsernamePattern)
            .WithMessage("PPPoE Username must be 3 to 40 characters: letters, digits, '.', '_' or '-'.");
        RuleFor(x => x.PlanName).NotEmpty().WithName("Internet Plan").MaximumLength(100);
        RuleFor(x => x.NapBox).MaximumLength(50);
        RuleFor(x => x.OnuSerial).MaximumLength(50);
        RuleFor(x => x.OnuModel).MaximumLength(100);
    }
}

public enum CreateCustomerOutcome
{
    Created,
    ValidationFailed,
    DuplicatePppoeUsername,
    ManagerOnlyNotAllowed,
}

public sealed record CreateCustomerResult(
    CreateCustomerOutcome Outcome,
    int? CustomerId = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);

public sealed record CreatedCustomerDto(int Id, string Status);

/// <summary>Provisional dropdown options until modules 05 (Plans) and 06 (NAP) provide real lists.</summary>
public sealed record CustomerFormOptionsDto(IReadOnlyList<string> Plans, IReadOnlyList<string> NapBoxes);

public interface ICustomerCommands
{
    Task<CreateCustomerResult> CreateAsync(CreateCustomerRequest request, string actorUserId, bool actorIsManager,
        CancellationToken cancellationToken);
}
