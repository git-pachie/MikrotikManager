using Microsoft.AspNetCore.Identity;
using Netora.Application.Abstractions;
using Netora.Application.Auth;
using Netora.Domain.Security;

namespace Netora.Infrastructure.Identity;

internal sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IJwtTokenService tokenService,
    ISecurityEventWriter securityEvents) : IAuthService
{
    // Used to spend similar time when the user does not exist, so timing does not reveal valid user names.
    private static readonly ApplicationUser DummyUser = new() { UserName = "dummy" };

    public async Task<LoginResult> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByNameAsync(request.UserName);

        if (user is null)
        {
            passwordHasher.HashPassword(DummyUser, request.Password);
            await Fail(null, request.UserName, ipAddress, "UnknownUser", cancellationToken);
            return new LoginResult(LoginOutcome.InvalidCredentials, null);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            await Fail(user.Id, request.UserName, ipAddress, "LockedOut", cancellationToken);
            return new LoginResult(LoginOutcome.LockedOut, null);
        }

        if (!user.IsActive)
        {
            await Fail(user.Id, request.UserName, ipAddress, "Disabled", cancellationToken);
            return new LoginResult(LoginOutcome.InvalidCredentials, null);
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            await userManager.AccessFailedAsync(user);
            if (await userManager.IsLockedOutAsync(user))
            {
                await securityEvents.WriteAsync(SecurityEventTypes.LockoutTriggered, false, user.Id,
                    request.UserName, ipAddress, "TooManyFailedAttempts", cancellationToken);
                await Fail(user.Id, request.UserName, ipAddress, "WrongPassword", cancellationToken);
                return new LoginResult(LoginOutcome.LockedOut, null);
            }

            await Fail(user.Id, request.UserName, ipAddress, "WrongPassword", cancellationToken);
            return new LoginResult(LoginOutcome.InvalidCredentials, null);
        }

        await userManager.ResetAccessFailedCountAsync(user);

        var roles = await userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault(r => Roles.All.Contains(r));
        if (role is null)
        {
            // An account without a Manager or Staff role has no permissions at all.
            await Fail(user.Id, request.UserName, ipAddress, "NoRole", cancellationToken);
            return new LoginResult(LoginOutcome.InvalidCredentials, null);
        }

        user.LastSignInAtUtc = DateTimeOffset.UtcNow;
        await userManager.UpdateAsync(user);

        var (token, expires) = tokenService.Create(user, role);
        await securityEvents.WriteAsync(SecurityEventTypes.LoginSucceeded, true, user.Id,
            user.UserName, ipAddress, null, cancellationToken);

        return new LoginResult(LoginOutcome.Succeeded,
            new LoginResultDto(token, expires, ToDto(user, role)));
    }

    public async Task<CurrentUserDto?> GetCurrentUserAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null || !user.IsActive) return null;

        var roles = await userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault(r => Roles.All.Contains(r));
        return role is null ? null : ToDto(user, role);
    }

    public async Task RecordLogoutAsync(string userId, string? ipAddress, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId);
        await securityEvents.WriteAsync(SecurityEventTypes.Logout, true, userId, user?.UserName,
            ipAddress, null, cancellationToken);
    }

    private Task Fail(string? userId, string typedUserName, string? ip, string reason, CancellationToken ct)
        => securityEvents.WriteAsync(SecurityEventTypes.LoginFailed, false, userId, typedUserName, ip, reason, ct);

    internal static CurrentUserDto ToDto(ApplicationUser user, string role)
        => new(user.Id, user.UserName ?? string.Empty,
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName ?? string.Empty : user.DisplayName, role,
            user.MustChangePassword, user.ThemePreference, user.Email);
}
