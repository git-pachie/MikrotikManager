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
    public const string RouterAddress = "Router.Address";
    public const string RouterPort = "Router.Port";
    public const string RouterUserName = "Router.UserName";
    public const string RouterPassword = "Router.Password";

    public const string EmailEnabled = "Email.Enabled";
    public const string EmailFrom = "Email.From";
    public const string EmailFromName = "Email.FromName";
    public const string EmailAppBaseUrl = "Email.AppBaseUrl";
}
