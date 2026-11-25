using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Netora.Domain.Settings;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Router;

/// <summary>
/// Router:Mode chooses the router implementation. "Mock" (development without a router) or "RouterOS" (the real
/// RouterOS 7 REST API over HTTPS). There is deliberately no silent default in Production: the API refuses to start
/// until the mode is set, so mock results can never be mistaken for a real router.
/// </summary>
public sealed class RouterOptions
{
    public const string SectionName = "Router";
    public const string MockMode = "Mock";
    public const string RouterOsMode = "RouterOS";

    public string? Mode { get; set; }

    /// <summary>Per-call timeout for the RouterOS REST API.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    public MockRouterOptions Mock { get; set; } = new();

    public bool IsMock => string.Equals(Mode, MockMode, StringComparison.OrdinalIgnoreCase);
    public bool IsRouterOs => string.Equals(Mode, RouterOsMode, StringComparison.OrdinalIgnoreCase);
}

public sealed class MockRouterOptions
{
    /// <summary>JSON file with the mock router's secrets, relative to the API folder. Delete it to reset the mock.</summary>
    public string StatePath { get; set; } = "App_Data/mock-router.json";

    /// <summary>When true, every change sent to the mock fails, to test failures and Retry.</summary>
    public bool FailWrites { get; set; }
}

/// <summary>Connection details saved on the Settings page (13.02). The password is decrypted only here, server-side.</summary>
public sealed record RouterConnection(string Address, int Port, string UserName, string Password, string? CertificateThumbprint, string? Name);

public interface IRouterConnectionProvider
{
    /// <summary>Null while address, user name or password is missing.</summary>
    Task<RouterConnection?> GetAsync(CancellationToken cancellationToken);
}

internal sealed class RouterConnectionProvider(NetoraDbContext db, IDataProtectionProvider protection) : IRouterConnectionProvider
{
    public const string PasswordPurpose = "Netora.Router.Password.v1";

    public async Task<RouterConnection?> GetAsync(CancellationToken cancellationToken)
    {
        var s = await db.AppSettings.AsNoTracking().Where(x => x.Key.StartsWith("Router."))
            .ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken);
        string? V(string key) => s.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

        var address = V(AppSettingKeys.RouterAddress);
        var user = V(AppSettingKeys.RouterUserName);
        var protectedPassword = V(AppSettingKeys.RouterPassword);
        if (address is null || user is null || protectedPassword is null) return null;

        var password = protection.CreateProtector(PasswordPurpose).Unprotect(protectedPassword);
        var port = int.TryParse(V(AppSettingKeys.RouterPort), out var p) ? p : 443;
        return new RouterConnection(address, port, user, password, V(AppSettingKeys.RouterCertificateThumbprint), V(AppSettingKeys.RouterName));
    }
}
