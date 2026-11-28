using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using Netora.Application.Settings;
using Netora.Domain.Settings;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Approvals;

/// <summary>
/// Saved email settings with configuration fallback for installations that have yet to save SMTP settings.
/// </summary>
public interface IEmailSettingsProvider
{
    Task<EmailOptions> GetAsync(CancellationToken cancellationToken);
}

internal sealed class EmailSettingsProvider(NetoraDbContext db, IOptionsMonitor<EmailOptions> configured,
    IDataProtectionProvider protection) : IEmailSettingsProvider
{
    internal static IDataProtector PasswordProtector(IDataProtectionProvider protection, string? host, int port,
        SmtpSecurityMode security, string? userName)
        => protection.CreateProtector("Netora.Email.Smtp.Password.v1",
            FormattableString.Invariant($"{host?.Trim('[', ']').ToLowerInvariant()}|{port}|{security}|{userName}"));

    private static readonly string[] Keys =
        [
            AppSettingKeys.EmailEnabled, AppSettingKeys.EmailFrom, AppSettingKeys.EmailFromName, AppSettingKeys.EmailAppBaseUrl,
            AppSettingKeys.EmailSmtpHost, AppSettingKeys.EmailSmtpPort, AppSettingKeys.EmailSmtpUserName,
            AppSettingKeys.EmailSmtpPassword, AppSettingKeys.EmailSmtpSecurity, AppSettingKeys.EmailSmtpTimeoutSeconds,
            AppSettingKeys.EmailSmtpCheckCertificateRevocation,
        ];

    public async Task<EmailOptions> GetAsync(CancellationToken cancellationToken)
    {
        var c = configured.CurrentValue;
        var saved = await db.AppSettings.AsNoTracking().Where(s => Keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, cancellationToken);
        string? Value(string key, string? fallback = null) => saved.TryGetValue(key, out var row) ? row.Value : fallback;
        bool Flag(string key, bool fallback) => bool.TryParse(Value(key), out var value) ? value : fallback;
        int Number(string key, int fallback) => int.TryParse(Value(key), out var value) ? value : fallback;
        var defaults = c.Smtp;
        var security = Enum.TryParse<SmtpSecurityMode>(Value(AppSettingKeys.EmailSmtpSecurity), out var mode) && Enum.IsDefined(mode)
            ? mode : defaults.EffectiveSecurity;
        var smtp = new SmtpOptions
        {
            Host = Value(AppSettingKeys.EmailSmtpHost, defaults.Host),
            Port = Number(AppSettingKeys.EmailSmtpPort, defaults.Port),
            UserName = Value(AppSettingKeys.EmailSmtpUserName, defaults.UserName),
            Security = security,
            UseSsl = security != SmtpSecurityMode.None,
            TimeoutSeconds = Number(AppSettingKeys.EmailSmtpTimeoutSeconds, defaults.TimeoutSeconds),
            CheckCertificateRevocation = Flag(AppSettingKeys.EmailSmtpCheckCertificateRevocation, defaults.CheckCertificateRevocation),
        };
        smtp.Password = saved.TryGetValue(AppSettingKeys.EmailSmtpPassword, out var password)
            ? ReadPassword(password, smtp) : defaults.Password;

        return new EmailOptions
        {
            Enabled = Flag(AppSettingKeys.EmailEnabled, c.Enabled),
            From = Value(AppSettingKeys.EmailFrom, c.From),
            FromName = Value(AppSettingKeys.EmailFromName, c.FromName) ?? "Netora",
            AppBaseUrl = Value(AppSettingKeys.EmailAppBaseUrl, c.AppBaseUrl),
            Smtp = smtp,
        };
    }

    private string? ReadPassword(AppSetting setting, SmtpOptions smtp)
    {
        if (!setting.IsProtected || string.IsNullOrEmpty(setting.Value)) return null;
        try
        {
            return PasswordProtector(protection, smtp.Host, smtp.Port, smtp.EffectiveSecurity, smtp.UserName).Unprotect(setting.Value);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>Sends one message through the configured SMTP server. Used by approval emails and the Settings test email.</summary>
internal static class SmtpEmailSender
{
    public static async Task SendAsync(EmailOptions settings, MimeMessage message, CancellationToken cancellationToken)
    {
        var smtp = settings.Smtp;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(smtp.TimeoutSeconds, 5, 120)));
        using var client = new SmtpClient
        {
            Timeout = Math.Clamp(smtp.TimeoutSeconds, 5, 120) * 1000,
            // Normally on. Some antivirus products (for example AVG Mail Shield) replace the server certificate with
            // one that has no revocation information; only then may this be switched off for that machine.
            CheckCertificateRevocation = smtp.CheckCertificateRevocation,
        };
        await client.ConnectAsync(smtp.Host!, smtp.Port, smtp.SocketOptions, timeout.Token);
        if (!string.IsNullOrWhiteSpace(smtp.UserName))
            await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? string.Empty, timeout.Token);
        await client.SendAsync(message, timeout.Token);
        await client.DisconnectAsync(true, timeout.Token);
    }

    /// <summary>A short, single-line description of a send failure for logs and the Settings page. No secrets.</summary>
    public static string Describe(Exception ex)
    {
        var text = ex is MailKit.Security.SslHandshakeException && ex.Message.Contains("revocation", StringComparison.OrdinalIgnoreCase)
            ? "The secure connection was refused because the certificate revocation check failed. Antivirus email scanning " +
              "(for example AVG Mail Shield) can cause this. Review CheckCertificateRevocation under Email Configuration."
            : ex switch
            {
                MailKit.Security.AuthenticationException => "The SMTP server rejected the login. Check the username and password.",
                SmtpCommandException smtp => $"The SMTP server rejected the request ({smtp.StatusCode}). Check the sender and recipient settings.",
                OperationCanceledException => "The SMTP server did not respond in time.",
                _ => $"SMTP connection or send failed ({ex.GetType().Name}). Check the host, port and TLS settings.",
            };
        text = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return text.Length <= 400 ? text : text[..400];
    }
}
