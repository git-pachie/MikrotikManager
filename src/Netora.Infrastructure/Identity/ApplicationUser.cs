using Microsoft.AspNetCore.Identity;

namespace Netora.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Deactivated users cannot sign in and their existing tokens stop working.</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Set when a Manager creates the account or resets its password. Until the user picks a new password, the API
    /// allows only the profile and sign-out endpoints (13.01).
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>"light" or "dark"; null follows the browser's last choice (13.02 user theme preference).</summary>
    public string? ThemePreference { get; set; }

    public DateTimeOffset? LastSignInAtUtc { get; set; }
}
