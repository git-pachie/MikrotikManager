using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Netora.Application.Abstractions;
using Netora.Application.Auth;
using Netora.Application.Users;
using Netora.Domain.Security;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Identity;

/// <summary>The signed-in user's own account: display name, email, theme and password.</summary>
internal sealed class ProfileService(
    UserManager<ApplicationUser> users,
    NetoraDbContext db,
    IValidator<UpdateProfileRequest> validator,
    IJwtTokenService tokens,
    ISecurityEventWriter events,
    ILogger<ProfileService> logger) : IProfileService
{
    public async Task<ProfileDto?> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null || !user.IsActive) return null;
        return new ProfileDto(user.Id, user.UserName ?? string.Empty, UserAdminService.DisplayName(user), user.Email,
            await RoleOfAsync(user) ?? "None", user.ThemePreference, user.MustChangePassword);
    }

    public async Task<UserResult> UpdateAsync(string userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return new UserResult(UserOutcome.ValidationFailed, validation.Errors.GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
        }

        var user = await users.FindByIdAsync(userId);
        if (user is null || !user.IsActive) return new UserResult(UserOutcome.NotFound);

        var email = UserAdminService.Clean(request.Email);
        var displayName = request.DisplayName.Trim();
        if (displayName == user.DisplayName && string.Equals(email, user.Email, StringComparison.Ordinal))
            return new UserResult(UserOutcome.NoChanges);
        if (email is not null && !string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase)
            && await UserAdminService.EmailTakenAsync(db, email, user.Id, cancellationToken))
        {
            return new UserResult(UserOutcome.DuplicateEmail, new Dictionary<string, string[]>
            {
                [nameof(UpdateProfileRequest.Email)] = ["Another user already has this email address."],
            });
        }

        user.DisplayName = displayName;
        // Set directly: UserManager.SetEmailAsync would rotate the security stamp and sign the user out.
        UserAdminService.SetEmail(user, email);
        var saved = await users.UpdateAsync(user);
        if (!saved.Succeeded) return new UserResult(UserOutcome.Conflict);

        await events.WriteAsync(SecurityEventTypes.ProfileUpdated, true, user.Id, user.UserName, null, "Own profile", cancellationToken);
        return new UserResult(UserOutcome.Ok, UserId: user.Id);
    }

    public async Task<UserResult> SetThemeAsync(string userId, ThemeRequest request, CancellationToken cancellationToken)
    {
        var theme = request.Theme?.Trim().ToLowerInvariant();
        if (theme is not null && !UserRules.Themes.Contains(theme))
        {
            return new UserResult(UserOutcome.ValidationFailed, new Dictionary<string, string[]>
            {
                [nameof(ThemeRequest.Theme)] = ["Choose light or dark."],
            });
        }

        var user = await users.FindByIdAsync(userId);
        if (user is null || !user.IsActive) return new UserResult(UserOutcome.NotFound);
        if (user.ThemePreference == theme) return new UserResult(UserOutcome.NoChanges);
        user.ThemePreference = theme;
        var saved = await users.UpdateAsync(user);
        return new UserResult(saved.Succeeded ? UserOutcome.Ok : UserOutcome.Conflict, UserId: user.Id);
    }

    public async Task<UserResult> ChangePasswordAsync(string userId, ChangePasswordRequest request, string? ipAddress,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null || !user.IsActive) return new UserResult(UserOutcome.NotFound);

        if (string.IsNullOrEmpty(request.CurrentPassword))
            return Field(UserOutcome.ValidationFailed, nameof(ChangePasswordRequest.CurrentPassword), "Enter your current password.");
        if (request.NewPassword == request.CurrentPassword)
            return Field(UserOutcome.ValidationFailed, nameof(ChangePasswordRequest.NewPassword), "Choose a password different from the current one.");

        var problems = await UserAdminService.PasswordProblemsAsync(users, user, request.NewPassword);
        if (problems.Count > 0) return Field(UserOutcome.ValidationFailed, nameof(ChangePasswordRequest.NewPassword), problems.ToArray());

        if (!await users.CheckPasswordAsync(user, request.CurrentPassword))
        {
            // Counts towards lockout, like a failed sign-in.
            await users.AccessFailedAsync(user);
            await events.WriteAsync(SecurityEventTypes.PasswordChanged, false, user.Id, user.UserName, ipAddress, "WrongCurrentPassword", cancellationToken);
            return Field(UserOutcome.WrongPassword, nameof(ChangePasswordRequest.CurrentPassword), "The current password is not correct.");
        }

        var changed = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
            return Field(UserOutcome.ValidationFailed, nameof(ChangePasswordRequest.NewPassword), changed.Errors.Select(e => e.Description).ToArray());

        user.MustChangePassword = false;
        await users.ResetAccessFailedCountAsync(user);
        await users.UpdateAsync(user);
        await events.WriteAsync(SecurityEventTypes.PasswordChanged, true, user.Id, user.UserName, ipAddress, null, cancellationToken);
        logger.LogInformation("PasswordChanged UserId={UserId}", user.Id);

        // Changing the password rotates the security stamp, which ends the old token; issue a new one for this session.
        var role = await RoleOfAsync(user);
        if (role is null) return new UserResult(UserOutcome.Ok, UserId: user.Id);
        var (token, expires) = tokens.Create(user, role);
        return new UserResult(UserOutcome.Ok, UserId: user.Id, Login: new LoginResultDto(token, expires, AuthService.ToDto(user, role)));
    }

    private async Task<string?> RoleOfAsync(ApplicationUser user)
        => (await users.GetRolesAsync(user)).FirstOrDefault(r => Roles.All.Contains(r));

    private static UserResult Field(UserOutcome outcome, string field, params string[] messages)
        => new(outcome, new Dictionary<string, string[]> { [field] = messages });
}
