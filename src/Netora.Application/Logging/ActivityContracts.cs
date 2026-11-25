namespace Netora.Application.Logging;

/// <summary>
/// Operational event log (18-logging.md): one line per business event in the daily log file. Separate from the
/// SQL Audit Trail (module 12). Implementations never throw: logging must not break a request.
/// </summary>
public interface IActivityLog
{
    void Write(ActivityEvent activity);
}

public enum ActivityLevel
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// One event. <see cref="Event"/> is a stable identifier (for example <c>CustomerCreated</c>); do not rename events
/// without updating 18-logging.md. Never put passwords, tokens, personal data or free-text notes in any field:
/// <see cref="Target"/> is an entity type and ID, <see cref="Detail"/> is field names or a short reason class.
/// </summary>
/// <param name="Actor">User name. Leave null to use the signed-in user of the current request (or "anonymous").</param>
public sealed record ActivityEvent(
    string Category,
    string Event,
    string Outcome,
    string? Target = null,
    string? Detail = null,
    ActivityLevel Level = ActivityLevel.Information,
    string? Actor = null,
    Exception? Exception = null);

/// <summary>Category names from the table in 18.02.</summary>
public static class ActivityCategories
{
    public const string Authentication = "Authentication";
    public const string Customers = "Customers";
    public const string Pppoe = "PPPoE";
    public const string Approvals = "Approvals";
    public const string Users = "Users";
    public const string Settings = "Settings";
    public const string MikroTik = "MikroTik";
    public const string Application = "Application";
}

public static class ActivityOutcomes
{
    public const string Success = "Success";
    public const string Failure = "Failure";
    public const string Denied = "Denied";
    public const string Pending = "Pending";
    public const string Rejected = "Rejected";
}

public static class ActivityTargets
{
    public static string Customer(int id) => $"Customer:{id}";
    public static string Request(long id) => $"ApprovalRequest:{id}";
    public static string User(string id) => $"User:{id}";
}

/// <summary>Used where no file log is configured (tests, tools).</summary>
public sealed class NullActivityLog : IActivityLog
{
    public static readonly NullActivityLog Instance = new();

    public void Write(ActivityEvent activity) { }
}
