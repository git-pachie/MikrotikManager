using FluentValidation;

namespace Netora.Application.Customers;

/// <summary>Fields shared by the Add and Edit Customer forms (approved form customer-form.html).</summary>
public interface ICustomerFields
{
    string FirstName { get; }
    string LastName { get; }
    string? MobileNumber { get; }
    string? Email { get; }
    string Address { get; }
    string? Notes { get; }
    string PppoeUsername { get; }
    string PlanName { get; }
    string? NapBox { get; }
    string? OnuSerial { get; }
    string? OnuModel { get; }
    bool IsManagerOnly { get; }
}

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
    bool IsManagerOnly) : ICustomerFields;

/// <summary>One set of rules for both forms, so Add and Edit can never disagree about what is valid.</summary>
public abstract class CustomerFieldsValidator<T> : AbstractValidator<T> where T : ICustomerFields
{
    public const string PppoeUsernamePattern = "^[A-Za-z0-9._-]{3,40}$";
    public const string MobilePattern = @"^[0-9+()\- ]{7,30}$";

    protected CustomerFieldsValidator()
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

public sealed class CreateCustomerRequestValidator : CustomerFieldsValidator<CreateCustomerRequest>;

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

/// <summary>Active plans and NAP boxes for the customer form (NAP always includes UNKNOWN first).</summary>
public sealed record CustomerFormOptionsDto(IReadOnlyList<string> Plans, IReadOnlyList<string> NapBoxes);

/// <summary>Moves a customer to another NAP box (06.02). No approval and no router call.</summary>
public sealed record MoveNapRequest(string NapBox, string ConcurrencyStamp);

/// <summary>The signed-in user performing a command. Built from the server-side token, never from request data.</summary>
public sealed record CustomerActor(string UserId, string UserName, bool IsManager);

public interface ICustomerCommands
{
    Task<CreateCustomerResult> CreateAsync(CreateCustomerRequest request, CustomerActor actor,
        CancellationToken cancellationToken);

    Task<UpdateCustomerResult> UpdateAsync(int id, UpdateCustomerRequest request, CustomerActor actor,
        CancellationToken cancellationToken);

    /// <summary>Staff and Managers; same visibility and termination rules as editing.</summary>
    Task<UpdateCustomerResult> MoveNapAsync(int id, MoveNapRequest request, CustomerActor actor,
        CancellationToken cancellationToken);
}
