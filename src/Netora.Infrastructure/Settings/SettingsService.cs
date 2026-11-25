using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Netora.Application.Abstractions;
using Netora.Application.Auth;
using Netora.Application.Settings;
using Netora.Domain.Security;
using Netora.Domain.Settings;
using Netora.Infrastructure.Approvals;
using Netora.Infrastructure.Identity;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Settings;

/// <summary>
/// Settings page (13.02). Router details are stored in the database with the password encrypted (Data Protection)
/// and never returned. Email: Enabled, sender, sender name and link address are stored here and override
/// appsettings; the SMTP server and login stay in configuration and secrets.
/// </summary>
internal sealed class SettingsService(
    NetoraDbContext db,
    UserManager<ApplicationUser> users,
    IEmailSettingsProvider emailSettings,
    IDataProtectionProvider protection,
    IConfiguration configuration,
    Microsoft.Extensions.Options.IOptions<Router.RouterOptions> routerOptions,
    IValidator<UpdateRouterSettingsRequest> routerValidator,
    IValidator<UpdateEmailSettingsRequest> emailValidator,
    ISecurityEventWriter events,
    ILogger<SettingsService> logger) : ISettingsService
{
    public const int DefaultRouterPort = 443;
    private readonly IDataProtector routerProtector = protection.CreateProtector("Netora.Router.Password.v1");

    public async Task<SettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var saved = await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, cancellationToken);
        string? Value(string key) => saved.TryGetValue(key, out var s) ? s.Value : null;

        var address = Value(AppSettingKeys.RouterAddress);
        var routerUser = Value(AppSettingKeys.RouterUserName);
        var passwordSet = !string.IsNullOrEmpty(Value(AppSettingKeys.RouterPassword));
        var router = new RouterSettingsDto(Value(AppSettingKeys.RouterName), address,
            int.TryParse(Value(AppSettingKeys.RouterPort), out var port) ? port : DefaultRouterPort, routerUser, passwordSet,
            ConnectionStatus: Value(AppSettingKeys.RouterLastTestOk) switch { "true" => "Connected", "false" => "Failed", _ => "NotTested" },
            Mode: routerOptions.Value.IsRouterOs ? Router.RouterOptions.RouterOsMode : Router.RouterOptions.MockMode,
            CertificateThumbprint: Value(AppSettingKeys.RouterCertificateThumbprint),
            LastTestedAtUtc: DateTimeOffset.TryParse(Value(AppSettingKeys.RouterLastTestAtUtc), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var tested) ? tested : null,
            LastTestMessage: Value(AppSettingKeys.RouterLastTestMessage));

        var email = await emailSettings.GetAsync(cancellationToken);
        var managers = await users.GetUsersInRoleAsync(Roles.Manager);
        var activeManagers = managers.Where(u => u.IsActive).ToList();
        var withEmail = activeManagers.Count(u => !string.IsNullOrWhiteSpace(u.Email));
        var emailDto = new EmailSettingsDto(email.Enabled, email.From, email.FromName, email.AppBaseUrl, email.Smtp.Host, email.Smtp.Port,
            email.Smtp.UseSsl, !string.IsNullOrWhiteSpace(email.Smtp.UserName) && !string.IsNullOrEmpty(email.Smtp.Password),
            email.Smtp.CheckCertificateRevocation, withEmail, email.NotReadyReason);

        var routerSaved = !string.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(routerUser) && passwordSet;
        var bootstrapLeft = !string.IsNullOrWhiteSpace(configuration["Bootstrap:ManagerPassword"]);
        var setup = new List<SetupCheckDto>
        {
            new("Database", true, "Connected."),
            new("Manager account", activeManagers.Count > 0, $"{activeManagers.Count} active Manager(s)."),
            new("First Manager password removed from secrets", !bootstrapLeft,
                bootstrapLeft ? "Remove Bootstrap:ManagerUserName and Bootstrap:ManagerPassword from the secrets now that a Manager exists." : "Done."),
            new("Manager email addresses", withEmail > 0,
                withEmail > 0 ? $"{withEmail} of {activeManagers.Count} active Managers have an email address." : "No active Manager has an email address. Add one under User Management or My Profile."),
            new("Approval emails", email.NotReadyReason is null && emailDto.SmtpLoginSet,
                email.NotReadyReason switch
                {
                    "Disabled" => "Turned off. Switch on \"Send approval emails\" below.",
                    "NotConfigured" => "Set the SMTP server (appsettings) and the sender below.",
                    _ => emailDto.SmtpLoginSet ? "On." : "On, but the SMTP login is not set in the secrets.",
                }),
            new("MikroTik router details", routerSaved,
                !routerSaved ? "Enter the router address, user name and password below."
                : router.ConnectionStatus == "Connected" ? $"Saved and tested ({router.Mode})."
                : "Saved. Use Test Connection to check it."),
            new("Router mode", router.Mode == Router.RouterOptions.RouterOsMode,
                router.Mode == Router.RouterOptions.RouterOsMode ? "RouterOS: changes go to the real router." : "Mock: changes go to a pretend router (Router:Mode in appsettings). Set RouterOS to use the real router."),
        };

        return new SettingsDto(router, emailDto, setup);
    }

    public async Task<SettingsResult> UpdateRouterAsync(UpdateRouterSettingsRequest request, string actorUserId, CancellationToken cancellationToken)
    {
        var validation = await routerValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Invalid(validation);

        var now = DateTimeOffset.UtcNow;
        await SetAsync(AppSettingKeys.RouterName, Clean(request.Name), actorUserId, now, cancellationToken);
        await SetAsync(AppSettingKeys.RouterAddress, Clean(request.Address), actorUserId, now, cancellationToken);
        await SetAsync(AppSettingKeys.RouterPort, request.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), actorUserId, now, cancellationToken);
        await SetAsync(AppSettingKeys.RouterUserName, Clean(request.UserName), actorUserId, now, cancellationToken);
        await SetAsync(AppSettingKeys.RouterCertificateThumbprint, UpdateRouterSettingsRequest.NormalizeThumbprint(request.CertificateThumbprint), actorUserId, now, cancellationToken);
        var passwordChange = "kept";
        if (request.ClearPassword)
        {
            await SetAsync(AppSettingKeys.RouterPassword, null, actorUserId, now, cancellationToken, isProtected: true);
            passwordChange = "cleared";
        }
        else if (!string.IsNullOrEmpty(request.NewPassword))
        {
            await SetAsync(AppSettingKeys.RouterPassword, routerProtector.Protect(request.NewPassword), actorUserId, now, cancellationToken, isProtected: true);
            passwordChange = "changed";
        }
        await db.SaveChangesAsync(cancellationToken);

        // Field names only; the password value is never logged.
        await events.WriteAsync(SecurityEventTypes.SettingsUpdated, true, actorUserId, null, null, $"Router settings, password {passwordChange}", cancellationToken);
        logger.LogInformation("SettingsUpdated Section=Router Password={Password} ActorUserId={ActorUserId}", passwordChange, actorUserId);
        return new SettingsResult(SettingsOutcome.Ok);
    }

    public async Task<SettingsResult> UpdateEmailAsync(UpdateEmailSettingsRequest request, string actorUserId, CancellationToken cancellationToken)
    {
        var validation = await emailValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Invalid(validation);

        var now = DateTimeOffset.UtcNow;
        await SetAsync(AppSettingKeys.EmailEnabled, request.Enabled ? "true" : "false", actorUserId, now, cancellationToken);
        await SetAsync(AppSettingKeys.EmailFrom, Clean(request.From), actorUserId, now, cancellationToken);
        await SetAsync(AppSettingKeys.EmailFromName, Clean(request.FromName), actorUserId, now, cancellationToken);
        await SetAsync(AppSettingKeys.EmailAppBaseUrl, Clean(request.AppBaseUrl), actorUserId, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await events.WriteAsync(SecurityEventTypes.SettingsUpdated, true, actorUserId, null, null,
            $"Email settings, sending {(request.Enabled ? "on" : "off")}", cancellationToken);
        logger.LogInformation("SettingsUpdated Section=Email Enabled={Enabled} ActorUserId={ActorUserId}", request.Enabled, actorUserId);
        return new SettingsResult(SettingsOutcome.Ok);
    }

    public async Task<SettingsResult> SendTestEmailAsync(string actorUserId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(actorUserId);
        if (user is null || string.IsNullOrWhiteSpace(user.Email)) return new SettingsResult(SettingsOutcome.NoRecipient);

        // The test works while approval emails are switched off, so the settings can be checked before turning them on.
        var settings = await emailSettings.GetAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.Smtp.Host) || string.IsNullOrWhiteSpace(settings.From))
            return new SettingsResult(SettingsOutcome.NotConfigured);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.From));
        message.To.Add(new MailboxAddress(UserAdminService.DisplayName(user), user.Email));
        message.Subject = "[Netora] Test email";
        message.Body = new TextPart("plain")
        {
            Text = $"This is a test email from Netora, sent from the Settings page by {user.UserName}.\n\n" +
                   $"SMTP server: {settings.Smtp.Host}:{settings.Smtp.Port} ({settings.Smtp.SocketOptions}).\n" +
                   $"Approval emails are currently {(settings.Enabled ? "on" : "off")}.",
        };

        try
        {
            await SmtpEmailSender.SendAsync(settings, message, cancellationToken);
            logger.LogInformation("TestEmailSent ActorUserId={ActorUserId}", actorUserId);
            return new SettingsResult(SettingsOutcome.Ok, Test: new TestEmailResultDto(true, user.Email!, $"Sent to {user.Email}. Check the inbox (and the spam folder)."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var reason = SmtpEmailSender.Describe(ex);
            logger.LogWarning("TestEmailFailed ActorUserId={ActorUserId} Error={Error}", actorUserId, reason);
            return new SettingsResult(SettingsOutcome.Ok, Test: new TestEmailResultDto(false, user.Email!, reason));
        }
    }

    /// <summary>For sending to the router (module 09). Null when no password is stored.</summary>
    internal async Task<string?> RouterPasswordAsync(CancellationToken cancellationToken)
    {
        var stored = await db.AppSettings.AsNoTracking().Where(s => s.Key == AppSettingKeys.RouterPassword)
            .Select(s => s.Value).FirstOrDefaultAsync(cancellationToken);
        return string.IsNullOrEmpty(stored) ? null : routerProtector.Unprotect(stored);
    }

    private async Task SetAsync(string key, string? value, string actorUserId, DateTimeOffset now, CancellationToken ct, bool isProtected = false)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = value, IsProtected = isProtected, UpdatedAtUtc = now, UpdatedByUserId = actorUserId });
            return;
        }
        if (row.Value == value) return;
        row.Value = value;
        row.IsProtected = isProtected;
        row.UpdatedAtUtc = now;
        row.UpdatedByUserId = actorUserId;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SettingsResult Invalid(FluentValidation.Results.ValidationResult v) => new(SettingsOutcome.ValidationFailed,
        v.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
}
