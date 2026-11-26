using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Netora.Application.Abstractions;
using Netora.Application.Audit;
using Netora.Application.Auth;
using Netora.Application.Settings;
using Netora.Application.Users;
using Netora.Domain.Settings;
using Netora.Infrastructure.Audit;
using Netora.Infrastructure.Approvals;
using Netora.Infrastructure.Identity;
using Netora.Infrastructure.Persistence;
using Netora.Infrastructure.Settings;
using Netora.WebApi.Infrastructure;

namespace Netora.Tests;

/// <summary>A small service container over an isolated in-memory database, with real Identity.</summary>
public sealed class IdentityTestHost : IDisposable
{
    public const string Password = "Start#Pass123";
    private readonly ServiceProvider provider;

    public IdentityTestHost(Dictionary<string, string?>? config = null)
    {
        var name = Guid.NewGuid().ToString("N");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<NetoraDbContext>(o => o.UseInMemoryDatabase(name)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
        services.AddIdentityCore<ApplicationUser>(o =>
            {
                o.Password.RequiredLength = 10;
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<NetoraDbContext>();
        services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        services.AddSingleton<IOptions<JwtOptions>>(Options.Create(new JwtOptions
        {
            Issuer = "test", Audience = "test", SigningKey = "0123456789abcdef0123456789abcdef0123456789", LifetimeMinutes = 60,
        }));
        services.AddValidatorsFromAssemblyContaining<CreateUserRequestValidator>(includeInternalTypes: true);
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<ISecurityEventWriter, SecurityEventWriter>();
        services.AddScoped<IEmailSettingsProvider, EmailSettingsProvider>();
        services.AddScoped<IUserAdminService, UserAdminService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<ISettingsService, SettingsService>();
        provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.All) roles.CreateAsync(new IdentityRole(role)).GetAwaiter().GetResult();
    }

    public AsyncServiceScope Scope() => provider.CreateAsyncScope();

    public async Task<ApplicationUser> AddUserAsync(string userName, string role, string? email = null, bool active = true)
    {
        await using var scope = Scope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = userName, DisplayName = userName, Email = email, NormalizedEmail = email?.ToUpperInvariant(), IsActive = active };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(user, role);
        return user;
    }

    public async Task<ApplicationUser> FindAsync(string userName)
    {
        await using var scope = Scope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByNameAsync(userName))!;
    }

    public static ActorInfo Actor(ApplicationUser u) => new(u.Id, u.UserName!, null);

    public void Dispose() => provider.Dispose();
}

public class UserAndSettingsTests
{
    private static async Task<T> Use<T, TService>(IdentityTestHost host, Func<TService, Task<T>> action) where TService : notnull
    {
        await using var scope = host.Scope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static Task<UserResult> Admin(IdentityTestHost h, Func<IUserAdminService, Task<UserResult>> a) => Use(h, a);

    private static Task<UserResult> Profile(IdentityTestHost h, Func<IProfileService, Task<UserResult>> a) => Use(h, a);

    // ----- 13.01 user administration -----

    [Fact]
    public async Task New_users_get_a_temporary_password_they_must_change()
    {
        using var h = new IdentityTestHost();
        var boss = await h.AddUserAsync("boss", Roles.Manager);

        var r = await Admin(h, s => s.CreateAsync(new CreateUserRequest("staff03", "Staff Three", "s3@example.test", Roles.Staff, "Temp#Pass456"), IdentityTestHost.Actor(boss), default));

        Assert.Equal(UserOutcome.Ok, r.Outcome);
        var created = await h.FindAsync("staff03");
        Assert.True(created.MustChangePassword);
        Assert.True(created.IsActive);
        var list = await Use<UserListDto, IUserAdminService>(h, s => s.ListAsync(default));
        Assert.Equal(Roles.Staff, list.Items.Single(i => i.UserName == "staff03").Role);
        Assert.Equal(1, list.ActiveManagers);
    }

    [Fact]
    public async Task Create_refuses_duplicates_and_weak_passwords()
    {
        using var h = new IdentityTestHost();
        var boss = await h.AddUserAsync("boss", Roles.Manager, "boss@example.test");
        var actor = IdentityTestHost.Actor(boss);

        var dupName = await Admin(h, s => s.CreateAsync(new CreateUserRequest("BOSS", "x", null, Roles.Staff, "Temp#Pass456"), actor, default));
        var dupEmail = await Admin(h, s => s.CreateAsync(new CreateUserRequest("other", "x", "Boss@Example.test", Roles.Staff, "Temp#Pass456"), actor, default));
        var weak = await Admin(h, s => s.CreateAsync(new CreateUserRequest("weakling", "x", null, Roles.Staff, "short"), actor, default));
        var badRole = await Admin(h, s => s.CreateAsync(new CreateUserRequest("roleless", "x", null, "Admin", "Temp#Pass456"), actor, default));

        Assert.Equal(UserOutcome.DuplicateUserName, dupName.Outcome);
        Assert.Equal(UserOutcome.DuplicateEmail, dupEmail.Outcome);
        Assert.Equal(UserOutcome.ValidationFailed, weak.Outcome);
        Assert.True(weak.Errors!.ContainsKey(nameof(CreateUserRequest.TemporaryPassword)));
        Assert.Equal(UserOutcome.ValidationFailed, badRole.Outcome);
    }

    [Fact]
    public async Task The_last_active_manager_cannot_be_demoted_or_disabled()
    {
        using var h = new IdentityTestHost();
        var boss = await h.AddUserAsync("boss", Roles.Manager);
        var other = await h.AddUserAsync("other", Roles.Manager, active: false);
        var staff = await h.AddUserAsync("staff", Roles.Staff);
        var actor = IdentityTestHost.Actor(boss);

        // Reactivate the other Manager so it can be demoted, then try to demote the remaining one.
        var o = await h.FindAsync("other");
        Assert.Equal(UserOutcome.Ok, (await Admin(h, s => s.UpdateAsync(o.Id, new UpdateUserRequest("other", null, Roles.Manager, true, o.ConcurrencyStamp!), actor, default))).Outcome);
        o = await h.FindAsync("other");
        Assert.Equal(UserOutcome.Ok, (await Admin(h, s => s.UpdateAsync(o.Id, new UpdateUserRequest("other", null, Roles.Staff, true, o.ConcurrencyStamp!), actor, default))).Outcome);

        // Now boss is the only active Manager; another Manager (staff promoted) tries to remove boss.
        var st = await h.FindAsync("staff");
        await Admin(h, s => s.UpdateAsync(st.Id, new UpdateUserRequest("staff", null, Roles.Manager, true, st.ConcurrencyStamp!), actor, default));
        var b = await h.FindAsync("boss");
        var promoted = IdentityTestHost.Actor(await h.FindAsync("staff"));
        Assert.Equal(UserOutcome.Ok, (await Admin(h, s => s.UpdateAsync(b.Id, new UpdateUserRequest("boss", null, Roles.Staff, true, b.ConcurrencyStamp!), promoted, default))).Outcome);

        var last = await h.FindAsync("staff");
        var other2 = IdentityTestHost.Actor(await h.FindAsync("other"));
        Assert.Equal(UserOutcome.LastManager,
            (await Admin(h, s => s.UpdateAsync(last.Id, new UpdateUserRequest("staff", null, Roles.Manager, false, last.ConcurrencyStamp!), other2, default))).Outcome);
        Assert.Equal(UserOutcome.LastManager,
            (await Admin(h, s => s.UpdateAsync(last.Id, new UpdateUserRequest("staff", null, Roles.Staff, true, last.ConcurrencyStamp!), other2, default))).Outcome);
    }

    [Fact]
    public async Task Managers_cannot_demote_disable_or_reset_themselves()
    {
        using var h = new IdentityTestHost();
        var boss = await h.AddUserAsync("boss", Roles.Manager);
        await h.AddUserAsync("boss2", Roles.Manager);
        var actor = IdentityTestHost.Actor(boss);

        var demote = await Admin(h, s => s.UpdateAsync(boss.Id, new UpdateUserRequest("boss", null, Roles.Staff, true, boss.ConcurrencyStamp!), actor, default));
        var disable = await Admin(h, s => s.UpdateAsync(boss.Id, new UpdateUserRequest("boss", null, Roles.Manager, false, boss.ConcurrencyStamp!), actor, default));
        var reset = await Admin(h, s => s.ResetPasswordAsync(boss.Id, new ResetPasswordRequest("Temp#Pass456"), actor, default));
        var rename = await Admin(h, s => s.UpdateAsync(boss.Id, new UpdateUserRequest("The Boss", "boss@example.test", Roles.Manager, true, boss.ConcurrencyStamp!), actor, default));

        Assert.Equal(UserOutcome.SelfChangeNotAllowed, demote.Outcome);
        Assert.Equal(UserOutcome.SelfChangeNotAllowed, disable.Outcome);
        Assert.Equal(UserOutcome.SelfChangeNotAllowed, reset.Outcome);
        Assert.Equal(UserOutcome.Ok, rename.Outcome);
    }

    [Fact]
    public async Task Deleting_a_user_removes_access_and_preserves_audit_history()
    {
        using var h = new IdentityTestHost();
        var boss = await h.AddUserAsync("boss", Roles.Manager);
        var actor = IdentityTestHost.Actor(boss);
        var created = await Admin(h, s => s.CreateAsync(new CreateUserRequest("staff", "Staff", null, Roles.Staff,
            IdentityTestHost.Password), actor, default));
        var staff = await h.FindAsync("staff");
        var request = new DeleteUserRequest(staff.ConcurrencyStamp!);

        Assert.Equal(UserOutcome.Ok, (await Admin(h, s => s.DeleteAsync(staff.Id, request, actor, default))).Outcome);
        Assert.Null(await h.FindAsync("staff"));
        Assert.Equal(UserOutcome.NotFound, (await Admin(h, s => s.DeleteAsync(staff.Id, request, actor, default))).Outcome);
        var list = await Use<UserListDto, IUserAdminService>(h, s => s.ListAsync(default));
        Assert.Equal(0, list.ActiveStaff);
        Assert.DoesNotContain(list.Items, u => u.Id == staff.Id);

        await using var scope = h.Scope();
        var audit = new AuditQueries(scope.ServiceProvider.GetRequiredService<NetoraDbContext>());
        var trail = await audit.ListAsync(new AuditQuery(Search: "staff"), default);
        Assert.Equal(UserOutcome.Ok, created.Outcome);
        Assert.Equal(2, trail.TotalMatching);
        Assert.Contains(trail.Items, entry => entry is { Action: "Added user", Entity: "User staff", EntityLink: null });
        Assert.Contains(trail.Items, entry => entry is { Action: "Deleted user", Actor: "boss", Entity: "User staff", EntityLink: null });
    }

    [Fact]
    public async Task Deletion_rejects_self_deletion_the_last_active_manager_and_stale_confirmation()
    {
        using var h = new IdentityTestHost();
        var boss = await h.AddUserAsync("boss", Roles.Manager);
        var inactiveManager = await h.AddUserAsync("inactive", Roles.Manager, active: false);
        var staff = await h.AddUserAsync("staff", Roles.Staff);
        var actor = IdentityTestHost.Actor(boss);
        var request = new DeleteUserRequest(boss.ConcurrencyStamp!);

        Assert.Equal(UserOutcome.SelfDeleteNotAllowed, (await Admin(h, s => s.DeleteAsync(boss.Id, request, actor, default))).Outcome);
        Assert.Equal(UserOutcome.LastManager, (await Admin(h, s => s.DeleteAsync(boss.Id, request, IdentityTestHost.Actor(inactiveManager), default))).Outcome);
        Assert.Equal(UserOutcome.Conflict, (await Admin(h, s => s.DeleteAsync(staff.Id, new DeleteUserRequest("old-stamp"), actor, default))).Outcome);
        Assert.NotNull(await h.FindAsync("boss"));
        Assert.NotNull(await h.FindAsync("staff"));
    }

    [Fact]
    public async Task Disabling_or_changing_a_role_ends_sessions_and_stale_forms_conflict()
    {
        using var h = new IdentityTestHost();
        var boss = await h.AddUserAsync("boss", Roles.Manager);
        var staff = await h.AddUserAsync("staff", Roles.Staff);
        var actor = IdentityTestHost.Actor(boss);
        var stampBefore = staff.SecurityStamp;

        var off = await Admin(h, s => s.UpdateAsync(staff.Id, new UpdateUserRequest("staff", null, Roles.Staff, false, staff.ConcurrencyStamp!), actor, default));
        var stale = await Admin(h, s => s.UpdateAsync(staff.Id, new UpdateUserRequest("renamed", null, Roles.Staff, true, staff.ConcurrencyStamp!), actor, default));

        Assert.Equal(UserOutcome.Ok, off.Outcome);
        var after = await h.FindAsync("staff");
        Assert.False(after.IsActive);
        Assert.NotEqual(stampBefore, after.SecurityStamp);
        Assert.Equal(UserOutcome.Conflict, stale.Outcome);
    }

    [Fact]
    public async Task Password_reset_unlocks_requires_a_change_and_ends_sessions()
    {
        using var h = new IdentityTestHost();
        var boss = await h.AddUserAsync("boss", Roles.Manager);
        await h.AddUserAsync("staff", Roles.Staff);
        await using (var scope = h.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var s = await users.FindByNameAsync("staff");
            for (var i = 0; i < 5; i++) await users.AccessFailedAsync(s!);
            Assert.True(await users.IsLockedOutAsync(s!));
        }
        var before = await h.FindAsync("staff");

        var weak = await Admin(h, s => s.ResetPasswordAsync(before.Id, new ResetPasswordRequest("abc"), IdentityTestHost.Actor(boss), default));
        var ok = await Admin(h, s => s.ResetPasswordAsync(before.Id, new ResetPasswordRequest("Temp#Pass456"), IdentityTestHost.Actor(boss), default));

        Assert.Equal(UserOutcome.ValidationFailed, weak.Outcome);
        Assert.Equal(UserOutcome.Ok, ok.Outcome);
        await using var check = h.Scope();
        var um = check.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var after = (await um.FindByNameAsync("staff"))!;
        Assert.True(after.MustChangePassword);
        Assert.False(await um.IsLockedOutAsync(after));
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.True(await um.CheckPasswordAsync(after, "Temp#Pass456"));
        Assert.False(await um.CheckPasswordAsync(after, IdentityTestHost.Password));
    }

    // ----- My Profile -----

    [Fact]
    public async Task Changing_your_password_clears_the_temporary_flag_and_returns_a_new_token()
    {
        using var h = new IdentityTestHost();
        var u = await h.AddUserAsync("staff", Roles.Staff);
        await using (var scope = h.Scope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var s = await users.FindByIdAsync(u.Id);
            s!.MustChangePassword = true;
            await users.UpdateAsync(s);
        }

        var wrong = await Profile(h, p => p.ChangePasswordAsync(u.Id, new ChangePasswordRequest("nope", "New#Pass7890"), null, default));
        var same = await Profile(h, p => p.ChangePasswordAsync(u.Id, new ChangePasswordRequest(IdentityTestHost.Password, IdentityTestHost.Password), null, default));
        var ok = await Profile(h, p => p.ChangePasswordAsync(u.Id, new ChangePasswordRequest(IdentityTestHost.Password, "New#Pass7890"), null, default));

        Assert.Equal(UserOutcome.WrongPassword, wrong.Outcome);
        Assert.Equal(UserOutcome.ValidationFailed, same.Outcome);
        Assert.Equal(UserOutcome.Ok, ok.Outcome);
        Assert.NotNull(ok.Login);
        Assert.False(ok.Login!.User.MustChangePassword);
        var after = await h.FindAsync("staff");
        Assert.False(after.MustChangePassword);
        Assert.Equal(0, after.AccessFailedCount);
    }

    [Fact]
    public async Task Profile_saves_email_and_theme_and_refuses_taken_emails()
    {
        using var h = new IdentityTestHost();
        var u = await h.AddUserAsync("staff", Roles.Staff);
        await h.AddUserAsync("boss", Roles.Manager, "boss@example.test");
        var stampBefore = u.SecurityStamp;

        var taken = await Profile(h, p => p.UpdateAsync(u.Id, new UpdateProfileRequest("Staff", "BOSS@example.test"), default));
        var ok = await Profile(h, p => p.UpdateAsync(u.Id, new UpdateProfileRequest("Staff Person", "staff@example.test"), default));
        var badTheme = await Profile(h, p => p.SetThemeAsync(u.Id, new ThemeRequest("purple"), default));
        var dark = await Profile(h, p => p.SetThemeAsync(u.Id, new ThemeRequest("Dark"), default));

        Assert.Equal(UserOutcome.DuplicateEmail, taken.Outcome);
        Assert.Equal(UserOutcome.Ok, ok.Outcome);
        Assert.Equal(UserOutcome.ValidationFailed, badTheme.Outcome);
        Assert.Equal(UserOutcome.Ok, dark.Outcome);
        var after = await h.FindAsync("staff");
        Assert.Equal("staff@example.test", after.Email);
        Assert.Equal("dark", after.ThemePreference);
        // Editing your own email must not sign you out.
        Assert.Equal(stampBefore, after.SecurityStamp);
    }

    // ----- 13.02 settings -----

    [Fact]
    public async Task Router_password_is_stored_encrypted_and_never_returned()
    {
        using var h = new IdentityTestHost();
        var boss = await h.AddUserAsync("boss", Roles.Manager);

        await Use<SettingsResult, ISettingsService>(h, s => s.UpdateRouterAsync(
            new UpdateRouterSettingsRequest("Main", "192.168.88.1", 443, "netora-api", "Router#Secret1"), boss.Id, default));
        var view = await Use<SettingsDto, ISettingsService>(h, s => s.GetAsync(default));

        Assert.True(view.Router.PasswordSet);
        Assert.Equal("192.168.88.1", view.Router.Address);
        await using var scope = h.Scope();
        var stored = await scope.ServiceProvider.GetRequiredService<NetoraDbContext>().AppSettings.SingleAsync(s => s.Key == AppSettingKeys.RouterPassword);
        Assert.True(stored.IsProtected);
        Assert.DoesNotContain("Router#Secret1", stored.Value);
        var service = (SettingsService)scope.ServiceProvider.GetRequiredService<ISettingsService>();
        Assert.Equal("Router#Secret1", await service.RouterPasswordAsync(default));

        // Saving again without a new password keeps it; ClearPassword removes it.
        await Use<SettingsResult, ISettingsService>(h, s => s.UpdateRouterAsync(new UpdateRouterSettingsRequest("Main", "192.168.88.1", 443, "netora-api", null), boss.Id, default));
        Assert.True((await Use<SettingsDto, ISettingsService>(h, s => s.GetAsync(default))).Router.PasswordSet);
        await Use<SettingsResult, ISettingsService>(h, s => s.UpdateRouterAsync(new UpdateRouterSettingsRequest("Main", "192.168.88.1", 443, "netora-api", null, ClearPassword: true), boss.Id, default));
        Assert.False((await Use<SettingsDto, ISettingsService>(h, s => s.GetAsync(default))).Router.PasswordSet);
    }

    [Theory]
    [InlineData("http://192.168.88.1")]
    [InlineData("router/path")]
    public async Task Router_address_must_be_a_plain_host(string address)
    {
        using var h = new IdentityTestHost();
        var r = await Use<SettingsResult, ISettingsService>(h, s => s.UpdateRouterAsync(new UpdateRouterSettingsRequest(null, address, 443, null, null), "x", default));
        Assert.Equal(SettingsOutcome.ValidationFailed, r.Outcome);
    }

    [Fact]
    public async Task Saved_email_settings_override_configuration_but_not_the_smtp_login()
    {
        using var h = new IdentityTestHost(new Dictionary<string, string?>
        {
            ["Email:Enabled"] = "false",
            ["Email:From"] = "config@example.test",
            ["Email:Smtp:Host"] = "smtp.gmail.com",
            ["Email:Smtp:UserName"] = "login@example.test",
            ["Email:Smtp:Password"] = "app-password",
        });
        var boss = await h.AddUserAsync("boss", Roles.Manager, "boss@example.test");

        var needsSender = await Use<SettingsResult, ISettingsService>(h, s => s.UpdateEmailAsync(new UpdateEmailSettingsRequest(true, "", null, null), boss.Id, default));
        var saved = await Use<SettingsResult, ISettingsService>(h, s => s.UpdateEmailAsync(new UpdateEmailSettingsRequest(true, "netora@example.test", "Netora ISP", null), boss.Id, default));

        Assert.Equal(SettingsOutcome.ValidationFailed, needsSender.Outcome);
        Assert.Equal(SettingsOutcome.Ok, saved.Outcome);
        await using var scope = h.Scope();
        var effective = await scope.ServiceProvider.GetRequiredService<IEmailSettingsProvider>().GetAsync(default);
        Assert.True(effective.Enabled);
        Assert.Equal("netora@example.test", effective.From);
        Assert.Equal("Netora ISP", effective.FromName);
        Assert.Equal("login@example.test", effective.Smtp.UserName);
        var view = await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetAsync(default);
        Assert.True(view.Email.SmtpLoginSet);
        Assert.Equal(1, view.Email.ManagerRecipients);
        Assert.Contains(view.Setup, c => c.Item == "Approval emails" && c.Done);
    }

    [Fact]
    public async Task Test_email_needs_your_own_address_and_an_smtp_server()
    {
        using var h = new IdentityTestHost(new Dictionary<string, string?> { ["Email:From"] = "netora@example.test" });
        var noEmail = await h.AddUserAsync("boss", Roles.Manager);
        var withEmail = await h.AddUserAsync("boss2", Roles.Manager, "b2@example.test");

        var r1 = await Use<SettingsResult, ISettingsService>(h, s => s.SendTestEmailAsync(noEmail.Id, default));
        var r2 = await Use<SettingsResult, ISettingsService>(h, s => s.SendTestEmailAsync(withEmail.Id, default));

        Assert.Equal(SettingsOutcome.NoRecipient, r1.Outcome);
        Assert.Equal(SettingsOutcome.NotConfigured, r2.Outcome);
    }

    [Fact]
    public void Revocation_failures_are_explained_without_secrets()
    {
        var text = SmtpEmailSender.Describe(new MailKit.Security.SslHandshakeException(
            "The server's SSL certificate could not be validated:\n• The revocation function was unable to check revocation"));
        Assert.Contains("CheckCertificateRevocation", text);
        Assert.DoesNotContain("\n", text);
        Assert.True(new SmtpOptions().CheckCertificateRevocation);
    }

    // ----- password change required (API) -----

    [Theory]
    [InlineData("/api/v1/customers", 403)]
    [InlineData("/api/v1/approvals/5", 403)]
    [InlineData("/api/v1/profile/password", 200)]
    [InlineData("/api/v1/auth/logout", 200)]
    public async Task Temporary_passwords_only_reach_profile_and_auth(string path, int expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        context.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(PasswordChangeRequiredMiddleware.ClaimType, "true")], "test"));
        var middleware = new PasswordChangeRequiredMiddleware(c => { c.Response.StatusCode = 200; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        Assert.Equal(expected, context.Response.StatusCode);
    }
}
