using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Netora.Application.Abstractions;
using Netora.Application.Auth;
using Netora.Application.Users;
using Netora.Domain.Security;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Identity;

/// <summary>
/// Manager-controlled user administration (13.01). Rules: a Manager cannot demote, deactivate or reset their own
/// account here (My Profile is for that); the last active Manager cannot be demoted or deactivated; role changes,
/// deactivation and password resets rotate the security stamp, so the user's open sessions stop working at once.
/// </summary>
internal sealed class UserAdminService(
    UserManager<ApplicationUser> users,
    NetoraDbContext db,
    IValidator<CreateUserRequest> createValidator,
    IValidator<UpdateUserRequest> updateValidator,
    ISecurityEventWriter events,
    ILogger<UserAdminService> logger) : IUserAdminService
{
    public async Task<UserListDto> ListAsync(CancellationToken cancellationToken)
    {
        var roles = await RolesByUserAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var list = await users.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync(cancellationToken);
        var items = list.Select(u => new UserListItemDto(
            u.Id, u.UserName ?? string.Empty, DisplayName(u), u.Email, roles.GetValueOrDefault(u.Id, "None"), u.IsActive,
            u.LockoutEnd > now, u.MustChangePassword, u.LastSignInAtUtc)).ToList();
        return new UserListDto(items,
            items.Count(i => i.IsActive && i.Role == Roles.Manager),
            items.Count(i => i.IsActive && i.Role == Roles.Staff));
    }

    public async Task<UserDetailDto?> GetAsync(string id, string actorUserId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return null;
        var role = await RoleOfAsync(user);
        return new UserDetailDto(user.Id, user.UserName ?? string.Empty, DisplayName(user), user.Email, role, user.IsActive,
            user.LockoutEnd > DateTimeOffset.UtcNow, user.MustChangePassword, user.CreatedAtUtc, user.LastSignInAtUtc,
            user.ConcurrencyStamp ?? string.Empty, user.Id == actorUserId);
    }

    public async Task<UserResult> CreateAsync(CreateUserRequest request, ActorInfo actor, CancellationToken cancellationToken)
    {
        var validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Invalid(validation);

        var userName = request.UserName.Trim();
        var email = Clean(request.Email);
        if (await users.FindByNameAsync(userName) is not null)
            return Field(UserOutcome.DuplicateUserName, nameof(CreateUserRequest.UserName), "This user name is already in use.");
        if (email is not null && await EmailTakenAsync(email, null, cancellationToken))
            return Field(UserOutcome.DuplicateEmail, nameof(CreateUserRequest.Email), "Another user already has this email address.");

        var user = new ApplicationUser
        {
            UserName = userName,
            DisplayName = request.DisplayName.Trim(),
            Email = email,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            MustChangePassword = true,
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var created = await users.CreateAsync(user, request.TemporaryPassword);
        if (!created.Succeeded) return IdentityFailure(created, nameof(CreateUserRequest.TemporaryPassword));
        var role = await users.AddToRoleAsync(user, request.Role);
        if (!role.Succeeded) throw new InvalidOperationException("Could not assign the role: " + Describe(role));
        await transaction.CommitAsync(cancellationToken);

        await Event(SecurityEventTypes.UserCreated, user, actor, $"Role {request.Role}", cancellationToken);
        logger.LogInformation("UserCreated UserId={UserId} Role={Role} ActorUserId={ActorUserId}", user.Id, request.Role, actor.UserId);
        return new UserResult(UserOutcome.Ok, UserId: user.Id);
    }

    public async Task<UserResult> UpdateAsync(string id, UpdateUserRequest request, ActorInfo actor, CancellationToken cancellationToken)
    {
        var validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Invalid(validation);

        var user = await users.FindByIdAsync(id);
        if (user is null) return new UserResult(UserOutcome.NotFound);
        if (!string.Equals(user.ConcurrencyStamp, request.ConcurrencyStamp, StringComparison.Ordinal))
            return new UserResult(UserOutcome.Conflict);

        var currentRole = await RoleOfAsync(user);
        var roleChanged = !string.Equals(currentRole, request.Role, StringComparison.Ordinal);
        var activeChanged = user.IsActive != request.IsActive;
        if (user.Id == actor.UserId && (roleChanged || !request.IsActive))
            return new UserResult(UserOutcome.SelfChangeNotAllowed);

        // Losing a Manager: the account is an active Manager and stops being one.
        if (currentRole == Roles.Manager && user.IsActive && (request.Role != Roles.Manager || !request.IsActive)
            && await OtherActiveManagersAsync(user.Id, cancellationToken) == 0)
            return new UserResult(UserOutcome.LastManager);

        var email = Clean(request.Email);
        if (email is not null && !string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase)
            && await EmailTakenAsync(email, user.Id, cancellationToken))
            return Field(UserOutcome.DuplicateEmail, nameof(UpdateUserRequest.Email), "Another user already has this email address.");

        var displayName = request.DisplayName.Trim();
        var detailsChanged = displayName != user.DisplayName || !string.Equals(email, user.Email, StringComparison.Ordinal);
        if (!detailsChanged && !roleChanged && !activeChanged) return new UserResult(UserOutcome.NoChanges, UserId: user.Id);

        user.DisplayName = displayName;
        SetEmail(user, email);
        user.IsActive = request.IsActive;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (roleChanged)
            {
                if (Roles.All.Contains(currentRole)) Check(await users.RemoveFromRoleAsync(user, currentRole));
                Check(await users.AddToRoleAsync(user, request.Role));
            }
            // A new stamp ends the user's open sessions: needed for a role change or deactivation.
            var saved = roleChanged || (activeChanged && !request.IsActive)
                ? await users.UpdateSecurityStampAsync(user)
                : await users.UpdateAsync(user);
            if (!saved.Succeeded)
                return saved.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.ConcurrencyFailure))
                    ? new UserResult(UserOutcome.Conflict)
                    : IdentityFailure(saved, string.Empty);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new UserResult(UserOutcome.Conflict);
        }

        if (detailsChanged) await Event(SecurityEventTypes.UserUpdated, user, actor, "Details", cancellationToken);
        if (roleChanged) await Event(SecurityEventTypes.RoleChanged, user, actor, $"{currentRole} to {request.Role}", cancellationToken);
        if (activeChanged)
            await Event(request.IsActive ? SecurityEventTypes.UserReactivated : SecurityEventTypes.UserDeactivated, user, actor, null, cancellationToken);
        logger.LogInformation("UserUpdated UserId={UserId} RoleChanged={RoleChanged} Active={Active} ActorUserId={ActorUserId}",
            user.Id, roleChanged, user.IsActive, actor.UserId);
        return new UserResult(UserOutcome.Ok, UserId: user.Id);
    }

    public async Task<UserResult> ResetPasswordAsync(string id, ResetPasswordRequest request, ActorInfo actor, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return new UserResult(UserOutcome.NotFound);
        if (user.Id == actor.UserId) return new UserResult(UserOutcome.SelfChangeNotAllowed);

        var errors = await PasswordProblemsAsync(users, user, request.TemporaryPassword);
        if (errors.Count > 0) return Field(UserOutcome.ValidationFailed, nameof(ResetPasswordRequest.TemporaryPassword), errors.ToArray());

        // A temporary password the user must replace; also unlocks the account and ends open sessions.
        user.PasswordHash = users.PasswordHasher.HashPassword(user, request.TemporaryPassword);
        user.MustChangePassword = true;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        var saved = await users.UpdateSecurityStampAsync(user);
        if (!saved.Succeeded) return new UserResult(UserOutcome.Conflict);

        await Event(SecurityEventTypes.PasswordResetByManager, user, actor, null, cancellationToken);
        logger.LogInformation("PasswordResetByManager UserId={UserId} ActorUserId={ActorUserId}", user.Id, actor.UserId);
        return new UserResult(UserOutcome.Ok, UserId: user.Id);
    }

    internal static async Task<List<string>> PasswordProblemsAsync(UserManager<ApplicationUser> users, ApplicationUser user, string? password)
    {
        if (string.IsNullOrEmpty(password)) return ["Enter a password."];
        var problems = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, password);
            problems.AddRange(result.Errors.Select(e => e.Description));
        }
        return problems;
    }

    internal static void SetEmail(ApplicationUser user, string? email)
    {
        if (string.Equals(user.Email, email, StringComparison.Ordinal)) return;
        user.Email = email;
        user.NormalizedEmail = email?.ToUpperInvariant();
        user.EmailConfirmed = false;
    }

    internal static string DisplayName(ApplicationUser u) => string.IsNullOrWhiteSpace(u.DisplayName) ? u.UserName ?? string.Empty : u.DisplayName;

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static Task<bool> EmailTakenAsync(NetoraDbContext db, string email, string? exceptUserId, CancellationToken ct)
    {
        var normalized = email.ToUpperInvariant();
        return db.Users.AnyAsync(u => u.NormalizedEmail == normalized && u.Id != exceptUserId, ct);
    }

    private Task<bool> EmailTakenAsync(string email, string? exceptUserId, CancellationToken ct) => EmailTakenAsync(db, email, exceptUserId, ct);

    private async Task<string> RoleOfAsync(ApplicationUser user)
        => (await users.GetRolesAsync(user)).FirstOrDefault(r => Roles.All.Contains(r)) ?? "None";

    private async Task<Dictionary<string, string>> RolesByUserAsync(CancellationToken ct)
    {
        var rows = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id select new { ur.UserId, r.Name })
            .ToListAsync(ct);
        // Manager wins if an account somehow has both roles.
        return rows.GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Any(x => x.Name == Roles.Manager) ? Roles.Manager : g.First().Name ?? "None");
    }

    private async Task<int> OtherActiveManagersAsync(string exceptUserId, CancellationToken ct)
    {
        var managerRoleId = await db.Roles.Where(r => r.Name == Roles.Manager).Select(r => r.Id).FirstOrDefaultAsync(ct);
        return await (from ur in db.UserRoles
                      join u in db.Users on ur.UserId equals u.Id
                      where ur.RoleId == managerRoleId && u.IsActive && u.Id != exceptUserId
                      select u.Id).CountAsync(ct);
    }

    private Task Event(string type, ApplicationUser target, ActorInfo actor, string? detail, CancellationToken ct)
        => events.WriteAsync(type, true, target.Id, target.UserName, actor.IpAddress,
            Truncate(detail is null ? $"by {actor.UserName}" : $"{detail}, by {actor.UserName}", 128), ct);

    private static string Truncate(string v, int max) => v.Length <= max ? v : v[..max];

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException(Describe(result));
    }

    private static string Describe(IdentityResult result) => string.Join("; ", result.Errors.Select(e => e.Description));

    private static UserResult Invalid(FluentValidation.Results.ValidationResult v) => new(UserOutcome.ValidationFailed,
        v.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));

    private static UserResult Field(UserOutcome outcome, string field, params string[] messages)
        => new(outcome, new Dictionary<string, string[]> { [field] = messages });

    private static UserResult IdentityFailure(IdentityResult result, string passwordField)
    {
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateUserName)))
            return Field(UserOutcome.DuplicateUserName, nameof(CreateUserRequest.UserName), "This user name is already in use.");
        return Field(UserOutcome.ValidationFailed, passwordField, result.Errors.Select(e => e.Description).ToArray());
    }
}
