using FluentValidation;

namespace Netora.Application.Settings;

/// <summary>
/// MikroTik connection details (13.02). The password is write-only: only whether one is stored is ever returned.
/// Connecting and testing come with module 09.
/// </summary>
public sealed record RouterSettingsDto(string? Name, string? Address, int Port, string? UserName, bool PasswordSet, string ConnectionStatus);

/// <summary>
/// Email notification settings. Enabled, sender and links are edited on the Settings page; the SMTP server and login
/// stay in configuration (login in user-secrets or appsettings.Secrets.json) and are shown read-only.
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
    string? NotReadyReason);

public sealed record SetupCheckDto(string Item, bool Done, string Detail);

public sealed record SettingsDto(RouterSettingsDto Router, EmailSettingsDto Email, IReadOnlyList<SetupCheckDto> Setup);

/// <param name="NewPassword">Leave empty to keep the stored password.</param>
public sealed record UpdateRouterSettingsRequest(string? Name, string? Address, int Port, string? UserName, string? NewPassword, bool ClearPassword = false);

public sealed record UpdateEmailSettingsRequest(bool Enabled, string? From, string? FromName, string? AppBaseUrl);

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
        RuleFor(x => x.AppBaseUrl).Must(v => Uri.TryCreate(v, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
            .WithMessage("Enter a full address such as https://netora.example/, or leave it empty.")
            .When(x => !string.IsNullOrWhiteSpace(x.AppBaseUrl));
    }
}
