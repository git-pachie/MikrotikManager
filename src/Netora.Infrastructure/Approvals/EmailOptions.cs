using MailKit.Security;
using Netora.Application.Settings;

namespace Netora.Infrastructure.Approvals;

/// <summary>
/// SMTP settings for approval emails (08.03). Emails are sent only when <see cref="Enabled"/> is true and
/// <see cref="SmtpOptions.Host"/> and <see cref="From"/> are set; otherwise notices are logged as skipped.
/// Settings saved by a Manager override legacy SMTP configuration.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Master switch. False (the default) sends nothing, whatever the other settings are.</summary>
    public bool Enabled { get; set; }

    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>Sender address, for example netora@your-isp.example. For Gmail, use the Gmail address you sign in with.</summary>
    public string? From { get; set; }

    public string FromName { get; set; } = "Netora";

    /// <summary>Optional public address of the WebUI, used for links in emails, for example https://netora.example/.</summary>
    public string? AppBaseUrl { get; set; }

    /// <summary>Why emails cannot be sent, or null when they can.</summary>
    public string? NotReadyReason => Enabled ? ConfigurationIssue : "Disabled";

    public string? ConfigurationIssue =>
        string.IsNullOrWhiteSpace(Smtp.Host) || string.IsNullOrWhiteSpace(From) ? "NotConfigured"
        : !string.IsNullOrWhiteSpace(Smtp.UserName) && string.IsNullOrEmpty(Smtp.Password) ? "PasswordRequired"
        : null;
}

public sealed class SmtpOptions
{
    /// <summary>For example smtp.gmail.com.</summary>
    public string? Host { get; set; }

    /// <summary>587 (STARTTLS, default) or 465 (SSL from the start). Gmail supports both.</summary>
    public int Port { get; set; } = 587;

    /// <summary>
    /// True (default): the connection is always encrypted. Port 465 uses SSL from the start; any other port upgrades
    /// with STARTTLS, and sending fails if the server cannot encrypt. False: no encryption, only for an internal relay.
    /// </summary>
    public bool UseSsl { get; set; } = true;

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Check whether the server certificate was revoked (default true). Set false only on a machine where antivirus
    /// email scanning replaces the certificate (for example AVG Mail Shield); trust and host name are still checked.
    /// </summary>
    public bool CheckCertificateRevocation { get; set; } = true;

    public SmtpSecurityMode? Security { get; set; }

    public SmtpSecurityMode EffectiveSecurity => Security
        ?? (!UseSsl ? SmtpSecurityMode.None : Port == 465 ? SmtpSecurityMode.TlsOnConnect : SmtpSecurityMode.StartTls);

    public SecureSocketOptions SocketOptions => EffectiveSecurity switch
    {
        SmtpSecurityMode.None => SecureSocketOptions.None,
        SmtpSecurityMode.TlsOnConnect => SecureSocketOptions.SslOnConnect,
        _ => SecureSocketOptions.StartTls,
    };
}
