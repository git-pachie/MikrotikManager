using FluentValidation;
using Netora.Application.Auth;

namespace Netora.Application.Users;

public sealed record UserListItemDto(
    string Id,
    string UserName,
    string DisplayName,
    string? Email,
    string Role,
    bool IsActive,
    bool IsLockedOut,
    bool MustChangePassword,
    DateTimeOffset? LastSignInAtUtc);

public sealed record UserListDto(IReadOnlyList<UserListItemDto> Items, int ActiveManagers, int ActiveStaff);

public sealed record UserDetailDto(
    string Id,
    string UserName,
    string DisplayName,
    string? Email,
    string Role,
    bool IsActive,
    bool IsLockedOut,
    bool MustChangePassword,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastSignInAtUtc,
    string ConcurrencyStamp,
    bool IsCurrentUser);

/// <param name="TemporaryPassword">The user must replace it at first sign-in.</param>
public sealed record CreateUserRequest(string UserName, string DisplayName, string? Email, string Role, string TemporaryPassword);

public sealed record UpdateUserRequest(string DisplayName, string? Email, string Role, bool IsActive, string ConcurrencyStamp);

public sealed record DeleteUserRequest(string ConcurrencyStamp);

public sealed record ResetPasswordRequest(string TemporaryPassword);

public sealed record ProfileDto(string Id, string UserName, string DisplayName, string? Email, string Role, string? ThemePreference,
    bool MustChangePassword);

public sealed record UpdateProfileRequest(string DisplayName, string? Email);

public sealed record ThemeRequest(string? Theme);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public enum UserOutcome
{
    Ok,
    NoChanges,
    ValidationFailed,
    NotFound,
    DuplicateUserName,
    DuplicateEmail,
    Conflict,

    /// <summary>The change would leave no active Manager.</summary>
    LastManager,

    /// <summary>Managers cannot demote, deactivate or reset themselves here; they use My Profile.</summary>
    SelfChangeNotAllowed,

    SelfDeleteNotAllowed,

    /// <summary>The current password given for a password change is wrong.</summary>
    WrongPassword,
}

/// <param name="Login">A fresh token after the signed-in user changed their own password (the old one stops working).</param>
public sealed record UserResult(UserOutcome Outcome, IReadOnlyDictionary<string, string[]>? Errors = null, string? UserId = null,
    LoginResultDto? Login = null);

/// <summary>Manager-only user administration (13.01).</summary>
public interface IUserAdminService
{
    Task<UserListDto> ListAsync(CancellationToken cancellationToken);
    Task<UserDetailDto?> GetAsync(string id, string actorUserId, CancellationToken cancellationToken);
    Task<UserResult> CreateAsync(CreateUserRequest request, ActorInfo actor, CancellationToken cancellationToken);
    Task<UserResult> UpdateAsync(string id, UpdateUserRequest request, ActorInfo actor, CancellationToken cancellationToken);
    Task<UserResult> DeleteAsync(string id, DeleteUserRequest request, ActorInfo actor, CancellationToken cancellationToken);
    Task<UserResult> ResetPasswordAsync(string id, ResetPasswordRequest request, ActorInfo actor, CancellationToken cancellationToken);
}

/// <summary>The signed-in user's own account.</summary>
public interface IProfileService
{
    Task<ProfileDto?> GetAsync(string userId, CancellationToken cancellationToken);
    Task<UserResult> UpdateAsync(string userId, UpdateProfileRequest request, CancellationToken cancellationToken);
    Task<UserResult> SetThemeAsync(string userId, ThemeRequest request, CancellationToken cancellationToken);
    Task<UserResult> ChangePasswordAsync(string userId, ChangePasswordRequest request, string? ipAddress, CancellationToken cancellationToken);
}

public sealed record ActorInfo(string UserId, string UserName, string? IpAddress);

public static class UserRules
{
    public const string UserNamePattern = "^[A-Za-z0-9._-]{3,40}$";
    public static readonly string[] Themes = ["light", "dark"];
}

public sealed class CreateUserRequestValidator : AbstractValidator<CreateUserRequest>
{
    public CreateUserRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().WithName("User name").Matches(UserRules.UserNamePattern)
            .WithMessage("User name must be 3 to 40 characters: letters, digits, '.', '_' or '-'.");
        RuleFor(x => x.DisplayName).NotEmpty().WithName("Display name").MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Role).Must(r => Roles.All.Contains(r)).WithMessage("Choose Manager or Staff.");
        RuleFor(x => x.TemporaryPassword).NotEmpty().WithName("Temporary password").MaximumLength(128);
    }
}

public sealed class UpdateUserRequestValidator : AbstractValidator<UpdateUserRequest>
{
    public UpdateUserRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().WithName("Display name").MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Role).Must(r => Roles.All.Contains(r)).WithMessage("Choose Manager or Staff.");
        RuleFor(x => x.ConcurrencyStamp).NotEmpty().MaximumLength(64);
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().WithName("Display name").MaximumLength(100);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.Email));
    }
}
