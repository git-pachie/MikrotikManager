using FluentValidation;
using Netora.Application.Customers;

namespace Netora.Application.Catalog;

// ----- Internet Plans (module 05) -----

/// <param name="Subscribers">Customers on this plan, any status. Manager-only customers are not counted for Staff.</param>
public sealed record PlanListItemDto(
    int Id, string Name, decimal? MonthlyFee, int? DownloadMbps, int? UploadMbps, string RouterProfile, bool IsActive, int Subscribers);

public sealed record PlanListDto(IReadOnlyList<PlanListItemDto> Items);

public sealed record PlanDetailDto(
    int Id, string Name, decimal? MonthlyFee, int? DownloadMbps, int? UploadMbps, string RouterProfile, bool IsActive,
    int Subscribers, string ConcurrencyStamp, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc);

/// <param name="ConcurrencyStamp">Required when editing: the value read when the form was opened.</param>
public sealed record SavePlanRequest(
    string Name, decimal? MonthlyFee, int? DownloadMbps, int? UploadMbps, string RouterProfile, bool IsActive,
    string? ConcurrencyStamp = null);

/// <summary>Result of "Check router": which plans' PPP profiles exist on the router. Nothing is changed.</summary>
public sealed record PlanProfileCheckDto(
    string Mode, bool Reachable, string? Error, DateTimeOffset CheckedAtUtc,
    IReadOnlyList<PlanProfileStatusDto> Plans, IReadOnlyList<string> UnusedRouterProfiles);

public sealed record PlanProfileStatusDto(int PlanId, string Name, string RouterProfile, bool IsActive, bool Found);

public sealed class SavePlanRequestValidator : AbstractValidator<SavePlanRequest>
{
    public SavePlanRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithName("Plan name").MaximumLength(100)
            .Must(CatalogRules.IsOneLine).WithMessage("Plan name must be on one line.");
        RuleFor(x => x.RouterProfile).NotEmpty().WithName("MikroTik profile").MaximumLength(100)
            .Must(CatalogRules.IsOneLine).WithMessage("MikroTik profile must be on one line.");
        RuleFor(x => x.MonthlyFee).InclusiveBetween(0, 1_000_000).WithName("Monthly fee")
            .Must(v => v is null || decimal.Round(v.Value, 2) == v.Value).WithMessage("Monthly fee can have at most 2 decimals.");
        RuleFor(x => x.DownloadMbps).InclusiveBetween(1, 100_000).WithName("Download speed");
        RuleFor(x => x.UploadMbps).InclusiveBetween(1, 100_000).WithName("Upload speed");
        RuleFor(x => x.ConcurrencyStamp).MaximumLength(32);
    }
}

public interface IPlanService
{
    Task<PlanListDto> ListAsync(bool viewerIsManager, CancellationToken cancellationToken);

    Task<PlanDetailDto?> GetAsync(int id, bool viewerIsManager, CancellationToken cancellationToken);

    /// <summary>Managers only.</summary>
    Task<CatalogResult> CreateAsync(SavePlanRequest request, CustomerActor actor, CancellationToken cancellationToken);

    /// <summary>Managers only. Renaming or remapping never touches customers or the router.</summary>
    Task<CatalogResult> UpdateAsync(int id, SavePlanRequest request, CustomerActor actor, CancellationToken cancellationToken);

    /// <summary>Lists the router's PPP profiles and compares them with the plans. Read only.</summary>
    Task<PlanProfileCheckDto> CheckRouterAsync(CancellationToken cancellationToken);
}

// ----- NAP Boxes (module 06) -----

/// <param name="Customers">Assigned customers, any status. Manager-only customers are not counted for Staff.</param>
public sealed record NapListItemDto(int Id, string Code, string? Area, string? Description, bool IsActive, bool IsSystem, int Customers);

public sealed record NapListDto(IReadOnlyList<NapListItemDto> Items);

/// <param name="CanMove">Whether the viewer may move this customer to another NAP (not for Staff on terminated customers).</param>
public sealed record NapCustomerDto(
    int CustomerId, string DisplayId, string Name, string PppoeUsername, string PlanName, string Status, string ConcurrencyStamp, bool CanMove);

/// <param name="MoveTargets">Active NAP codes a customer here can be moved to.</param>
public sealed record NapDetailDto(
    int Id, string Code, string? Area, string? Description, bool IsActive, bool IsSystem, string ConcurrencyStamp,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc,
    IReadOnlyList<NapCustomerDto> AssignedCustomers, IReadOnlyList<string> MoveTargets);

public sealed record SaveNapRequest(string Code, string? Area, string? Description, bool IsActive, string? ConcurrencyStamp = null);

public sealed class SaveNapRequestValidator : AbstractValidator<SaveNapRequest>
{
    public const string CodePattern = @"^[A-Za-z0-9][A-Za-z0-9 ._\-]{0,49}$";

    public SaveNapRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().WithName("NAP code").MaximumLength(50)
            .Matches(CodePattern).WithMessage("NAP code must be 1 to 50 characters: letters, digits, spaces, '.', '_' or '-', starting with a letter or digit.")
            .Must(c => !string.Equals(c?.Trim(), Domain.Network.NapBox.UnknownCode, StringComparison.OrdinalIgnoreCase))
            .WithMessage("UNKNOWN is reserved for customers without a NAP.");
        RuleFor(x => x.Area).MaximumLength(100).Must(CatalogRules.IsOneLine).WithMessage("Area must be on one line.");
        RuleFor(x => x.Description).MaximumLength(200).Must(CatalogRules.IsOneLine).WithMessage("Description must be on one line.");
        RuleFor(x => x.ConcurrencyStamp).MaximumLength(32);
    }
}

public interface INapService
{
    Task<NapListDto> ListAsync(bool viewerIsManager, CancellationToken cancellationToken);

    /// <summary>Assigned customers are filtered by the viewer's role first (Manager-only customers hidden from Staff).</summary>
    Task<NapDetailDto?> GetAsync(int id, bool viewerIsManager, CancellationToken cancellationToken);

    /// <summary>Managers only.</summary>
    Task<CatalogResult> CreateAsync(SaveNapRequest request, CustomerActor actor, CancellationToken cancellationToken);

    /// <summary>Managers only. UNKNOWN cannot be changed.</summary>
    Task<CatalogResult> UpdateAsync(int id, SaveNapRequest request, CustomerActor actor, CancellationToken cancellationToken);
}

// ----- Shared -----

public enum CatalogOutcome
{
    Ok,
    NoChanges,
    ValidationFailed,
    NotFound,
    Forbidden,

    /// <summary>Plan name or NAP code already used.</summary>
    Duplicate,

    /// <summary>Changed by someone else since the form was opened.</summary>
    Conflict,

    /// <summary>The built-in UNKNOWN NAP cannot be edited.</summary>
    Locked,
}

public sealed record CatalogResult(CatalogOutcome Outcome, int? Id = null, IReadOnlyDictionary<string, string[]>? Errors = null);

public sealed record CatalogSavedDto(int Id, bool Changed);

public static class CatalogRules
{
    public static bool IsOneLine(string? value) => value is null || !value.Any(char.IsControl);
}
