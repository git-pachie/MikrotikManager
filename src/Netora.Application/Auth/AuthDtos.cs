using FluentValidation;

namespace Netora.Application.Auth;

public sealed record LoginRequest(string UserName, string Password);

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

/// <param name="MustChangePassword">The user must pick a new password before doing anything else.</param>
/// <param name="Theme">"light", "dark" or null (no saved preference).</param>
public sealed record CurrentUserDto(string Id, string UserName, string DisplayName, string Role,
    bool MustChangePassword = false, string? Theme = null, string? Email = null);

public sealed record LoginResultDto(string AccessToken, DateTimeOffset ExpiresAtUtc, CurrentUserDto User);

public enum LoginOutcome
{
    Succeeded,
    InvalidCredentials,
    LockedOut
}

public sealed record LoginResult(LoginOutcome Outcome, LoginResultDto? Data);
