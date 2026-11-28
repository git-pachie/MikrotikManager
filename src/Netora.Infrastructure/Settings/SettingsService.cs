using System.Security.Cryptography;
using FluentValidation;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MimeKit;
using Netora.Application.Abstractions;
using Netora.Application.Auth;
using Netora.Application.Logging;
using Netora.Application.Settings;
using Netora.Domain.Security;
using Netora.Domain.Settings;
using Netora.Infrastructure.Approvals;
using Netora.Infrastructure.Identity;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Settings;

/// <summary>
/// Settings page (13.02). Router and SMTP passwords are stored using separate Data Protection purposes.
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
    ILogger<SettingsService> logger,
    IActivityLog? activity = null) : ISettingsService
{
    public const int DefaultRouterPort = 443;
    private readonly IDataProtector routerProtector = protection.CreateProtector(Router.RouterConnectionProvider.PasswordPurpose);
    private static readonly string[] RouterConnectionKeys =
    [
        AppSettingKeys.RouterMode, AppSettingKeys.RouterName, AppSettingKeys.RouterAddress,
        AppSettingKeys.RouterPort, AppSettingKeys.RouterUserName, AppSettingKeys.RouterPassword,
        AppSettingKeys.RouterCertificateThumbprint,
    ];

    public async Task<SettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var saved = await db.AppSettings.AsNoTracking().ToDictionaryAsync(s => s.Key, cancellationToken);
        string? Value(string key) => saved.TryGetValue(key, out var s) ? s.Value : null;

        var address = Value(AppSettingKeys.RouterAddress);
        var routerUser = Value(AppSettingKeys.RouterUserName);
        var storedPassword = Value(AppSettingKeys.RouterPassword);
        var passwordSet = !string.IsNullOrEmpty(storedPassword);
        var passwordUnreadable = false;
        if (passwordSet)
        {
            try
            {
                routerProtector.Unprotect(storedPassword!);
            }
            catch (CryptographicException)
            {
                passwordUnreadable = true;
            }
        }
        var mode = Router.RouterModeSelection.Resolve(Value(AppSettingKeys.RouterMode), routerOptions.Value);
        DateTimeOffset? lastTested = null;
        if (Value(AppSettingKeys.RouterLastTestMode) == mode
            && DateTimeOffset.TryParse(Value(AppSettingKeys.RouterLastTestAtUtc), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var tested)
            && !RouterConnectionKeys.Any(key => saved.TryGetValue(key, out var setting) && setting.UpdatedAtUtc > tested)
            && (mode == Router.RouterOptions.MockMode || !passwordUnreadable))
        {
            lastTested = tested;
        }

        var lastTestOk = lastTested is null ? null : Value(AppSettingKeys.RouterLastTestOk);
        var router = new RouterSettingsDto(Value(AppSettingKeys.RouterName), address,
            int.TryParse(Value(AppSettingKeys.RouterPort), out var port) ? port : DefaultRouterPort, routerUser, passwordSet,
            ConnectionStatus: lastTestOk switch { "true" => "Connected", "false" => "Failed", _ => "NotTested" },
            Mode: mode,
            CertificateThumbprint: Value(AppSettingKeys.RouterCertificateThumbprint),
            LastTestedAtUtc: lastTested,
            LastTestMessage: lastTested is null ? null : Value(AppSettingKeys.RouterLastTestMessage),
            PasswordUnreadable: passwordUnreadable);

        var email = await emailSettings.GetAsync(cancellationToken);
        var managers = await users.GetUsersInRoleAsync(Roles.Manager);
        var activeManagers = managers.Where(u => u.IsActive).ToList();
        var withEmail = activeManagers.Count(u => !string.IsNullOrWhiteSpace(u.Email));
        var emailDto = new EmailSettingsDto(email.Enabled, email.From, email.FromName, email.AppBaseUrl, email.Smtp.Host, email.Smtp.Port,
            email.Smtp.UseSsl, !string.IsNullOrWhiteSpace(email.Smtp.UserName) && !string.IsNullOrEmpty(email.Smtp.Password),
            email.Smtp.CheckCertificateRevocation, withEmail, email.NotReadyReason, email.Smtp.UserName,
            !string.IsNullOrEmpty(email.Smtp.Password), email.Smtp.EffectiveSecurity, email.Smtp.TimeoutSeconds);

        var routerSaved = !string.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(routerUser) && passwordSet && !passwordUnreadable;
        var bootstrapLeft = !string.IsNullOrWhiteSpace(configuration["Bootstrap:ManagerPassword"]);
        var setup = new List<SetupCheckDto>
        {
            new("Database", true, "Connected."),
            new("Manager account", activeManagers.Count > 0, $"{activeManagers.Count} active Manager(s)."),
            new("First Manager password removed from secrets", !bootstrapLeft,
                bootstrapLeft ? "Remove Bootstrap:ManagerUserName and Bootstrap:ManagerPassword from the secrets now that a Manager exists." : "Done."),
            new("Manager email addresses", withEmail > 0,
                withEmail > 0 ? $"{withEmail} of {activeManagers.Count} active Managers have an email address." : "No active Manager has an email address. Add one under User Management or My Profile."),
            new("Approval emails", email.NotReadyReason is null,
                email.NotReadyReason switch
                {
                    "Disabled" => "Turned off. Switch on \"Send approval emails\" below.",
                    "NotConfigured" => "Set the SMTP server and sender below.",
                    "PasswordRequired" => "Enter the SMTP password below.",
                    _ => "On.",
                }),
            new("MikroTik router details", routerSaved,
                passwordUnreadable ? Router.RouterConnectionProvider.UnreadablePasswordMessage
                : !routerSaved ? "Enter the router address, user name and password below."
                : router.ConnectionStatus == "Connected" ? $"Saved and tested ({router.Mode})."
                : "Saved. Use Test Connection to check it."),
            new("Router mode", router.Mode == Router.RouterOptions.RouterOsMode,
                router.Mode == Router.RouterOptions.RouterOsMode ? "Router changes go to the real MikroTik." : "Select RouterOS above to use the real MikroTik."),
        };

        return new SettingsDto(router, emailDto, setup);
    }

    public async Task<SettingsResult> UpdateRouterAsync(UpdateRouterSettingsRequest request, string actorUserId, CancellationToken cancellationToken)
    {
        var validation = await routerValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Invalid(validation);

        if (request.Mode is not (null or Router.RouterOptions.MockMode or Router.RouterOptions.RouterOsMode))
            return InvalidField(nameof(request.Mode), "Select Mock or RouterOS.");

        if (request.Mode == Router.RouterOptions.RouterOsMode)
        {
            if (string.IsNullOrWhiteSpace(request.Address))
                return InvalidField(nameof(request.Address), "Enter the MikroTik address.");
            if (string.IsNullOrWhiteSpace(request.UserName))
                return InvalidField(nameof(request.UserName), "Enter the MikroTik API user name.");
            if (request.ClearPassword)
                return InvalidField(nameof(request.NewPassword), "Switch to Mock to remove the saved API password.");
            if (string.IsNullOrEmpty(request.NewPassword))
            {
                try
                {
                    if (string.IsNullOrEmpty(await RouterPasswordAsync(cancellationToken)))
                        return InvalidField(nameof(request.NewPassword), "Enter the MikroTik API password.");
                }
                catch (CryptographicException)
                {
                    return InvalidField(nameof(request.NewPassword), Router.RouterConnectionProvider.UnreadablePasswordMessage);
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        if (request.Mode is { } mode)
            await SetAsync(AppSettingKeys.RouterMode, mode, actorUserId, now, cancellationToken);
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
        var modeDetail = request.Mode is null ? "" : $", mode {request.Mode}";
        await events.WriteAsync(SecurityEventTypes.SettingsUpdated, true, actorUserId, null, null, $"Router settings, password {passwordChange}{modeDetail}", cancellationToken);
        logger.LogInformation("SettingsUpdated Section=Router Password={Password} Mode={Mode} ActorUserId={ActorUserId}", passwordChange, request.Mode, actorUserId);
        return new SettingsResult(SettingsOutcome.Ok);
    }

    public async Task<SettingsResult> UpdateEmailAsync(UpdateEmailSettingsRequest request, string actorUserId, CancellationToken cancellationToken)
    {
        var validation = await emailValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid) return Invalid(validation);

        var now = DateTimeOffset.UtcNow;
        if (request.Smtp is { } smtp)
        {
            var host = Clean(smtp.Host)?.Trim('[', ']');
            var login = Clean(smtp.UserName);
            var current = (await emailSettings.GetAsync(cancellationToken)).Smtp;
            var sameConnection = string.Equals(current.Host?.Trim('[', ']'), host, StringComparison.OrdinalIgnoreCase)
                && current.Port == smtp.Port && current.EffectiveSecurity == smtp.Security
                && string.Equals(current.UserName, login, StringComparison.Ordinal);

            if (request.Enabled && host is null)
                return InvalidEmailField("Host", "Enter the SMTP server before turning emails on.");

            var keepsPassword = login is not null && !smtp.ClearPassword && string.IsNullOrEmpty(smtp.NewPassword);
            if (keepsPassword && !sameConnection && !string.IsNullOrEmpty(current.Password))
                return InvalidEmailField("NewPassword", "Enter the SMTP password again when changing the connection or login.");
            if (request.Enabled && login is not null && (smtp.ClearPassword
                || (keepsPassword && (!sameConnection || string.IsNullOrEmpty(current.Password)))))
                return InvalidEmailField("NewPassword", "Enter the SMTP password before turning emails on.");

            await SetAsync(AppSettingKeys.EmailSmtpHost, host, actorUserId, now, cancellationToken);
            await SetAsync(AppSettingKeys.EmailSmtpPort, smtp.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), actorUserId, now, cancellationToken);
            await SetAsync(AppSettingKeys.EmailSmtpUserName, login, actorUserId, now, cancellationToken);
            await SetAsync(AppSettingKeys.EmailSmtpSecurity, smtp.Security.ToString(), actorUserId, now, cancellationToken);
            await SetAsync(AppSettingKeys.EmailSmtpTimeoutSeconds, smtp.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture), actorUserId, now, cancellationToken);
            await SetAsync(AppSettingKeys.EmailSmtpCheckCertificateRevocation, smtp.CheckCertificateRevocation ? "true" : "false",
                actorUserId, now, cancellationToken);

            var password = keepsPassword ? current.Password : smtp.ClearPassword || login is null ? null : smtp.NewPassword;
            var protectedPassword = string.IsNullOrEmpty(password) ? null
                : EmailSettingsProvider.PasswordProtector(protection, host, smtp.Port, smtp.Security, login).Protect(password);
            await SetAsync(AppSettingKeys.EmailSmtpPassword, protectedPassword, actorUserId, now, cancellationToken, isProtected: true);
        }
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
        if (settings.ConfigurationIssue is not null)
            return new SettingsResult(SettingsOutcome.NotConfigured);

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.From!));
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
            logger.LogInformation("TestEmailFailed ActorUserId={ActorUserId} Error={Error}", actorUserId, reason);
            (activity ?? NullActivityLog.Instance).Write(new ActivityEvent(ActivityCategories.Application, "EmailDeliveryFailed",
                ActivityOutcomes.Failure, Detail: $"email=Test error={ex.GetType().Name}", Level: ActivityLevel.Warning));
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

    private static SettingsResult InvalidEmailField(string field, string message)
        => InvalidField($"Smtp.{field}", message);

    private static SettingsResult InvalidField(string field, string message)
        => new(SettingsOutcome.ValidationFailed, new Dictionary<string, string[]> { [field] = [message] });

    private static SettingsResult Invalid(FluentValidation.Results.ValidationResult v) => new(SettingsOutcome.ValidationFailed,
        v.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
}
