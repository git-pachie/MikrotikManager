using FluentValidation;

namespace Netora.Application.Settings;

/// <summary>
/// MikroTik connection details (13.02). The password is write-only: only whether one is stored is ever returned.
/// Connecting and testing come with module 09.
/// </summary>
/// <param name="ConnectionStatus">NotTested, Connected or Failed: the result of the last Test Connection.</param>
/// <param name="Mode">Effective mode, "Mock" or "RouterOS".</param>
public sealed record RouterSettingsDto(string? Name, string? Address, int Port, string? UserName, bool PasswordSet, string ConnectionStatus,
    string Mode = "Mock", string? CertificateThumbprint = null, DateTimeOffset? LastTestedAtUtc = null, string? LastTestMessage = null,
    bool PasswordUnreadable = false);

/// <summary>
/// Email notification settings. The password is write-only.
/// </summary>
public sealed record EmailSettingsDto(
    bool Enabled,
    string? From,
    string FromName,
    string? AppBaseUrl,
    string? SmtpHost,
    int SmtpPort,
    bool UseSsl,
    bool SmtpLoginSet,
    bool CheckCertificateRevocation,
    int ManagerRecipients,
    string? NotReadyReason,
    string? SmtpUserName = null,
    bool SmtpPasswordSet = false,
    SmtpSecurityMode SmtpSecurity = SmtpSecurityMode.StartTls,
    int SmtpTimeoutSeconds = 30);

public sealed record SetupCheckDto(string Item, bool Done, string Detail);

public sealed record SettingsDto(RouterSettingsDto Router, EmailSettingsDto Email, IReadOnlyList<SetupCheckDto> Setup);

/// <param name="NewPassword">Leave empty to keep the stored password.</param>
/// <param name="CertificateThumbprint">Optional SHA-256 fingerprint of the router's HTTPS certificate (for a self-signed certificate).</param>
public sealed record UpdateRouterSettingsRequest(string? Name, string? Address, int Port, string? UserName, string? NewPassword, bool ClearPassword = false,
    string? CertificateThumbprint = null, string? Mode = null)
{
    /// <summary>Upper-case hex without separators, or null.</summary>
    public static string? NormalizeThumbprint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var hex = new string(value.Where(c => !char.IsWhiteSpace(c) && c != ':' && c != '-').ToArray()).ToUpperInvariant();
        return hex.Length == 0 ? null : hex;
    }
}

public enum SmtpSecurityMode { StartTls, TlsOnConnect, None }

public sealed record SmtpConfigurationRequest(string? Host, int Port, string? UserName, string? NewPassword,
    bool ClearPassword, SmtpSecurityMode Security, int TimeoutSeconds, bool CheckCertificateRevocation);

public sealed record UpdateEmailSettingsRequest(bool Enabled, string? From, string? FromName, string? AppBaseUrl,
    SmtpConfigurationRequest? Smtp = null);

public sealed record TestEmailResultDto(bool Sent, string Recipient, string Message);

public enum SettingsOutcome
{
    Ok,
    ValidationFailed,

    /// <summary>The signed-in user has no email address to send the test to.</summary>
    NoRecipient,

    /// <summary>SMTP server or sender is missing.</summary>
    NotConfigured,
}

public sealed record SettingsResult(SettingsOutcome Outcome, IReadOnlyDictionary<string, string[]>? Errors = null, TestEmailResultDto? Test = null);

public interface ISettingsService
{
    Task<SettingsDto> GetAsync(CancellationToken cancellationToken);
    Task<SettingsResult> UpdateRouterAsync(UpdateRouterSettingsRequest request, string actorUserId, CancellationToken cancellationToken);
    Task<SettingsResult> UpdateEmailAsync(UpdateEmailSettingsRequest request, string actorUserId, CancellationToken cancellationToken);

    /// <summary>Sends a test email to the signed-in user's own address, at once (not queued), and reports the result.</summary>
    Task<SettingsResult> SendTestEmailAsync(string actorUserId, CancellationToken cancellationToken);
}

public sealed class UpdateRouterSettingsRequestValidator : AbstractValidator<UpdateRouterSettingsRequest>
{
    public UpdateRouterSettingsRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(100).Must(OneLine).WithMessage("Router Name must be on one line.");
        RuleFor(x => x.Address).MaximumLength(253)
            .Matches(@"^[A-Za-z0-9.\-:\[\]]+$").WithMessage("Enter a host name or IP address, without http:// or a path.")
            .When(x => !string.IsNullOrWhiteSpace(x.Address));
        RuleFor(x => x.Port).InclusiveBetween(1, 65535);
        RuleFor(x => x.CertificateThumbprint)
            .Must(v => UpdateRouterSettingsRequest.NormalizeThumbprint(v) is { Length: 64 } h && h.All(Uri.IsHexDigit))
            .WithName("Certificate fingerprint")
            .WithMessage("Enter the SHA-256 fingerprint: 64 hexadecimal characters (colons and spaces are allowed), or leave it empty.")
            .When(x => !string.IsNullOrWhiteSpace(x.CertificateThumbprint));
        RuleFor(x => x.UserName).MaximumLength(100).Must(OneLine).WithMessage("User name must be on one line.");
        RuleFor(x => x.NewPassword!).MaximumLength(128).Must(OneLine).WithMessage("Password must be on one line.")
            .When(x => !string.IsNullOrEmpty(x.NewPassword));
    }

    private static bool OneLine(string? v) => v is null || !v.Any(char.IsControl);
}

public sealed class UpdateEmailSettingsRequestValidator : AbstractValidator<UpdateEmailSettingsRequest>
{
    public UpdateEmailSettingsRequestValidator()
    {
        RuleFor(x => x.From).EmailAddress().WithName("SMTP Sender").MaximumLength(256).When(x => !string.IsNullOrWhiteSpace(x.From));
        RuleFor(x => x.From).NotEmpty().WithName("SMTP Sender").WithMessage("Enter the sender address before turning emails on.")
            .When(x => x.Enabled);
        RuleFor(x => x.FromName).MaximumLength(100).Must(v => v is null || !v.Any(char.IsControl)).WithMessage("Sender name must be on one line.");
        RuleFor(x => x.AppBaseUrl).MaximumLength(300)
            .Must(v => Uri.TryCreate(v, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
            .WithMessage("Enter a full address such as https://netora.example/, or leave it empty.")
            .When(x => !string.IsNullOrWhiteSpace(x.AppBaseUrl));
        RuleFor(x => x.Smtp!).SetValidator(new SmtpConfigurationRequestValidator()).When(x => x.Smtp is not null);
    }
}

internal sealed class SmtpConfigurationRequestValidator : AbstractValidator<SmtpConfigurationRequest>
{
    public SmtpConfigurationRequestValidator()
    {
        RuleFor(x => x.Host).MaximumLength(253)
            .Must(h => string.IsNullOrWhiteSpace(h) || Uri.CheckHostName(h.Trim()) != UriHostNameType.Unknown)
            .WithMessage("Enter a host name or IP address without a scheme, port, or path.");
        RuleFor(x => x.Port).InclusiveBetween(1, 65535);
        RuleFor(x => x.UserName).MaximumLength(256).Must(OneLine).WithMessage("Enter the SMTP login on one line.");
        RuleFor(x => x.NewPassword).MaximumLength(256).Must(OneLine).WithMessage("Enter the SMTP password on one line.");
        RuleFor(x => x.NewPassword).Empty().WithMessage("Choose either a replacement password or password removal.")
            .When(x => x.ClearPassword);
        RuleFor(x => x.UserName).NotEmpty().WithMessage("Enter an SMTP login to use this password.")
            .When(x => !string.IsNullOrEmpty(x.NewPassword));
        RuleFor(x => x.Security).IsInEnum();
        RuleFor(x => x.Security).NotEqual(SmtpSecurityMode.None).WithMessage("Choose a TLS mode for an authenticated SMTP login.")
            .When(x => !string.IsNullOrWhiteSpace(x.UserName));
        RuleFor(x => x.TimeoutSeconds).InclusiveBetween(5, 120);
    }

    private static bool OneLine(string? value) => value is null || !value.Any(char.IsControl);
}
