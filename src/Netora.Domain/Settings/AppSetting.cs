namespace Netora.Domain.Settings;

/// <summary>
/// One application setting changed on the Settings page (13.02). Settings that are not stored here fall back to
/// appsettings files. Secrets are stored encrypted (<see cref="IsProtected"/>) and are never returned to clients.
/// </summary>
public sealed class AppSetting
{
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }

    /// <summary>True when <see cref="Value"/> is encrypted with ASP.NET Core Data Protection.</summary>
    public bool IsProtected { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public string? UpdatedByUserId { get; set; }
}

/// <summary>Stable setting keys.</summary>
public static class AppSettingKeys
{
    public const string RouterName = "Router.Name";
    public const string RouterMode = "Router.Mode";
    public const string RouterAddress = "Router.Address";
    public const string RouterPort = "Router.Port";
    public const string RouterUserName = "Router.UserName";
    public const string RouterPassword = "Router.Password";
    public const string RouterCertificateThumbprint = "Router.CertificateThumbprint";
    public const string RouterLastTestAtUtc = "Router.LastTestAtUtc";
    public const string RouterLastTestOk = "Router.LastTestOk";
    public const string RouterLastTestMessage = "Router.LastTestMessage";
    public const string RouterLastTestMode = "Router.LastTestMode";

    public const string EmailEnabled = "Email.Enabled";
    public const string EmailFrom = "Email.From";
    public const string EmailFromName = "Email.FromName";
    public const string EmailAppBaseUrl = "Email.AppBaseUrl";
    public const string EmailSmtpHost = "Email.Smtp.Host";
    public const string EmailSmtpPort = "Email.Smtp.Port";
    public const string EmailSmtpUserName = "Email.Smtp.UserName";
    public const string EmailSmtpPassword = "Email.Smtp.Password";
    public const string EmailSmtpSecurity = "Email.Smtp.Security";
    public const string EmailSmtpTimeoutSeconds = "Email.Smtp.TimeoutSeconds";
    public const string EmailSmtpCheckCertificateRevocation = "Email.Smtp.CheckCertificateRevocation";
}
