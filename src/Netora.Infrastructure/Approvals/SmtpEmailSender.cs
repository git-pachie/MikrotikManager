using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using Netora.Domain.Settings;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Approvals;

/// <summary>
/// The email settings in effect: appsettings (and secrets) for the SMTP server and login, overridden by the values
/// a Manager saved on the Settings page (Enabled, sender, sender name, link address).
/// </summary>
public interface IEmailSettingsProvider
{
    Task<EmailOptions> GetAsync(CancellationToken cancellationToken);
}

internal sealed class EmailSettingsProvider(NetoraDbContext db, IOptionsMonitor<EmailOptions> configured) : IEmailSettingsProvider
{
    private static readonly string[] Keys =
        [AppSettingKeys.EmailEnabled, AppSettingKeys.EmailFrom, AppSettingKeys.EmailFromName, AppSettingKeys.EmailAppBaseUrl];

    public async Task<EmailOptions> GetAsync(CancellationToken cancellationToken)
    {
        var c = configured.CurrentValue;
        var saved = await db.AppSettings.AsNoTracking().Where(s => Keys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        return new EmailOptions
        {
            Enabled = saved.TryGetValue(AppSettingKeys.EmailEnabled, out var enabled) && bool.TryParse(enabled, out var on) ? on : c.Enabled,
            From = saved.TryGetValue(AppSettingKeys.EmailFrom, out var from) ? from : c.From,
            FromName = saved.TryGetValue(AppSettingKeys.EmailFromName, out var name) && !string.IsNullOrWhiteSpace(name) ? name! : c.FromName,
            AppBaseUrl = saved.TryGetValue(AppSettingKeys.EmailAppBaseUrl, out var url) ? url : c.AppBaseUrl,
            Smtp = c.Smtp,
        };
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
              "(for example AVG Mail Shield) can cause this; see Email:Smtp:CheckCertificateRevocation in the README."
            : ex.GetType().Name + ": " + ex.Message;
        text = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return text.Length <= 400 ? text : text[..400];
    }
}
