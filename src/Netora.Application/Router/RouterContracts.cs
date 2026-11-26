namespace Netora.Application.Router;

/// <summary>A PPP secret as the router reports it. <see cref="Password"/> is for import only and never leaves the API.</summary>
public sealed record RouterSecret(
    string Id,
    string Name,
    string? Password,
    string? Profile,
    bool Disabled,
    string? Comment,
    string? RemoteAddress,
    string? LocalAddress,
    string? Service);

/// <summary>The values Netora wants on the router. A null <see cref="Password"/> leaves the router's password unchanged.</summary>
public sealed record RouterSecretWrite(
    string Name,
    string? Password,
    string Profile,
    bool Disabled,
    string? Comment,
    string? RemoteAddress,
    string? LocalAddress);

public sealed record RouterActiveSession(string Name, string? Address, string? Uptime, string? CallerId);

public sealed record RouterIdentity(string Identity, string? Version, string? Board);

public enum RouterErrorKind
{
    NotConfigured,
    Unreachable,
    Unauthorized,
    Certificate,
    Rejected,
    NotFound,
}

/// <summary>A router call failed. <see cref="Message"/> is safe to show to Managers: it never contains credentials.</summary>
public sealed class RouterException(RouterErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public RouterErrorKind Kind { get; } = kind;
}

/// <summary>
/// RouterOS 7 PPP access (09.01). Two implementations: the REST client over HTTPS (Router:Mode = RouterOS) and a mock
/// for development without a router (Router:Mode = Mock). Secrets are never deleted through this interface.
/// </summary>
public interface IRouterClient
{
    /// <summary>"RouterOS" or "Mock". Shown in the UI so mock results are never mistaken for a real router.</summary>
    string Mode { get; }

    Task<RouterIdentity> GetIdentityAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RouterSecret>> ListSecretsAsync(CancellationToken cancellationToken);

    /// <summary>By router ID first (survives a rename), then by name. Null when neither exists.</summary>
    Task<RouterSecret?> FindSecretAsync(string? id, string name, CancellationToken cancellationToken);

    Task<RouterSecret> CreateSecretAsync(RouterSecretWrite secret, CancellationToken cancellationToken);

    Task<RouterSecret> UpdateSecretAsync(string id, RouterSecretWrite secret, CancellationToken cancellationToken);

    Task<RouterActiveSession?> FindActiveSessionAsync(string name, CancellationToken cancellationToken);

    /// <summary>Names of the router's PPP profiles (/ppp/profile). Read only: Netora never creates or changes profiles.</summary>
    Task<IReadOnlyList<string>> ListProfilesAsync(CancellationToken cancellationToken);
}

/// <summary>Sends an approved request to the router and records the outcome on the request (08.04, 04.05).</summary>
public interface IRouterProvisioner
{
    /// <summary>
    /// Idempotent: a request that already succeeded is not sent again, and the router is set to the desired state
    /// rather than replaying a change. Never throws for router problems; they are recorded as Failed.
    /// </summary>
    Task<ProvisioningResult> ProvisionAsync(long requestId, string actorUserId, string actorUserName, CancellationToken cancellationToken);
}

public sealed record ProvisioningResult(string Status, string? Error, int Attempts);

/// <summary>Live router state for one customer, checked on demand when the profile is opened or refreshed (09.03).</summary>
public sealed record RouterStatusDto(
    string Mode,
    bool Reachable,
    string? Error,
    DateTimeOffset CheckedAtUtc,
    bool SecretFound,
    bool SecretExpected,
    string? SecretState,
    bool SessionActive,
    string? AssignedIp,
    string? Uptime,
    string? ActualProfile,
    string ExpectedProfile,
    IReadOnlyList<RouterMismatchDto> Mismatches);

/// <summary>A difference between SQL Server and the router. Shown, never fixed automatically (09.04).</summary>
public sealed record RouterMismatchDto(string Field, string Expected, string Actual);

public interface IRouterStatusService
{
    /// <summary>Null when the customer does not exist or is hidden from the viewer.</summary>
    Task<RouterStatusDto?> CheckAsync(int customerId, bool viewerIsManager, CancellationToken cancellationToken);
}

public sealed record ImportCandidateDto(string Name, string? Profile, bool Disabled, string? Comment, string State, string? Reason, int? CustomerId);

public sealed record ImportScanDto(string Mode, IReadOnlyList<ImportCandidateDto> Items, int NewCount);

public sealed record ImportRequest(IReadOnlyList<string> Names);

public sealed record ImportResultDto(int Imported, int Skipped, IReadOnlyList<string> Messages);

/// <summary>Import of existing router secrets (04.04): an on-demand scan, then an explicit confirmation.</summary>
public interface IPppoeImportService
{
    Task<ImportScanDto> ScanAsync(CancellationToken cancellationToken);

    Task<ImportResultDto> ImportAsync(ImportRequest request, string actorUserId, string actorUserName, CancellationToken cancellationToken);
}

public sealed record RouterTestResultDto(bool Connected, string Mode, string Message, DateTimeOffset TestedAtUtc);

public interface IRouterConnectionTester
{
    Task<RouterTestResultDto> TestAsync(string actorUserId, CancellationToken cancellationToken);
}

public static class RouterRules
{
    /// <summary>Marker appended to the comment of secrets Netora created, so a retry can recognise them.</summary>
    public static string Marker(int customerId) => $"[netora:NET-{customerId:0000}]";

    public static string Comment(int customerId, string? comment)
        => string.IsNullOrWhiteSpace(comment) ? Marker(customerId) : $"{comment.Trim()} {Marker(customerId)}";

    /// <summary>The comment without Netora's marker, for comparing and importing.</summary>
    public static string? StripMarker(string? comment)
    {
        if (string.IsNullOrWhiteSpace(comment)) return null;
        var i = comment.IndexOf("[netora:", StringComparison.Ordinal);
        var plain = (i >= 0 ? comment[..i] : comment).Trim();
        return plain.Length == 0 ? null : plain;
    }
}
