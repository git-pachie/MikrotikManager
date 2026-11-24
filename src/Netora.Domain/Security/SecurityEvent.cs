namespace Netora.Domain.Security;

/// <summary>Immutable record of a security-relevant event (login, lockout, bootstrap, logout).</summary>
public sealed class SecurityEvent
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public string EventType { get; set; } = string.Empty;
    public bool Succeeded { get; set; }

    /// <summary>Identity user ID when the account is known.</summary>
    public string? UserId { get; set; }

    /// <summary>User name as typed (control characters removed, length capped).</summary>
    public string? UserName { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>Short reason class such as WrongPassword or LockedOut. Never contains secrets.</summary>
    public string? Reason { get; set; }
}

public static class SecurityEventTypes
{
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string LockoutTriggered = "LockoutTriggered";
    public const string Logout = "Logout";
    public const string ManagerBootstrapped = "ManagerBootstrapped";
}
