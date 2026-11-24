using FluentValidation;

namespace Netora.Application.Customers;

/// <summary>Edit Customer form. <see cref="ConcurrencyStamp"/> is the value read when the form was opened.</summary>
public sealed record UpdateCustomerRequest(
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
    bool IsManagerOnly,
    string ConcurrencyStamp) : ICustomerFields;

public sealed class UpdateCustomerRequestValidator : CustomerFieldsValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.ConcurrencyStamp).NotEmpty().MaximumLength(32);
    }
}

public enum UpdateCustomerOutcome
{
    Updated,
    NoChanges,
    ValidationFailed,

    /// <summary>The customer does not exist, or the caller may not see it. Both look the same to the caller.</summary>
    NotFound,

    /// <summary>Terminated customer and the caller is Staff: view only. Managers may still edit.</summary>
    Locked,

    /// <summary>
    /// PPPoE Username and Internet Plan of an Active, Disabled or Terminated customer change only through an approved
    /// request.
    /// </summary>
    ServiceFieldsLocked,
    DuplicatePppoeUsername,
    ManagerOnlyNotAllowed,

    /// <summary>Someone else saved this customer after the form was opened.</summary>
    Conflict,
}

public sealed record UpdateCustomerResult(
    UpdateCustomerOutcome Outcome,
    IReadOnlyDictionary<string, string[]>? Errors = null);

public sealed record UpdatedCustomerDto(int Id, string ConcurrencyStamp, bool Changed);
