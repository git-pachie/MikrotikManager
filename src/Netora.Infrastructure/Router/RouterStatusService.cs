using Microsoft.EntityFrameworkCore;
using Netora.Application.Logging;
using Netora.Application.Router;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Router;

/// <summary>
/// On-demand router check for one customer (09.03): run when the customer profile is opened or refreshed, never on a
/// schedule. Compares SQL Server with the router and reports differences (09.04) without changing either side.
/// </summary>
internal sealed class RouterStatusService(NetoraDbContext db, IRouterClient router, IActivityLog? activity = null) : IRouterStatusService
{
    public async Task<PppoeTrafficSampleDto?> SampleTrafficAsync(int customerId, bool viewerIsManager, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId && (viewerIsManager || !c.IsManagerOnly))
            .Select(c => new { c.PppoeUsername, c.Status, c.PppoeDeletedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);
        if (customer is null) return null;
        if (customer.PppoeDeletedAtUtc is not null || customer.Status == CustomerStatus.PendingApproval)
            return new("no_account", DateTimeOffset.UtcNow);
        if (router.Mode == "Mock") return new("mock", DateTimeOffset.UtcNow);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var counters = await router.ReadPppoeTrafficAsync(customer.PppoeUsername, budget.Token);
            return new(counters is null ? "offline" : "online", DateTimeOffset.UtcNow, counters);
        }
        catch (RouterException)
        {
            return new("unavailable", DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new("unavailable", DateTimeOffset.UtcNow);
        }
    }

    public async Task<RouterStatusDto?> CheckAsync(int customerId, bool viewerIsManager, CancellationToken cancellationToken)
    {
        var customer = await FindVisibleAsync(customerId, viewerIsManager, cancellationToken);
        if (customer is null) return null;
        using var budget = RouterReadBudget(cancellationToken);
        return await CheckAsync(customer, checkSession: true, budget.Token, cancellationToken);
    }

    public async Task<PppoeDiagnosticsDto?> DiagnoseAsync(int customerId, bool viewerIsManager, bool includeLogs,
        CancellationToken cancellationToken)
    {
        var customer = await FindVisibleAsync(customerId, viewerIsManager, cancellationToken);
        if (customer is null) return null;
        using var budget = RouterReadBudget(cancellationToken);
        var status = await CheckAsync(customer, includeLogs, budget.Token, cancellationToken);
        if (!includeLogs || !status.Reachable) return new(status, null, null);

        try
        {
            return new(status, await router.ReadPppoeLogsAsync(customer.PppoeUsername, budget.Token), null);
        }
        catch (RouterException ex)
        {
            return new(status, null, "Router logs unavailable. " + DiagnosticError(ex.Kind));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(status, null, "Router logs unavailable. The router did not answer in time.");
        }
    }

    private Task<Customer?> FindVisibleAsync(int customerId, bool viewerIsManager, CancellationToken cancellationToken)
        => db.Customers.AsNoTracking().Include(c => c.Plan)
            .FirstOrDefaultAsync(c => c.Id == customerId && (viewerIsManager || !c.IsManagerOnly), cancellationToken);

    private async Task<RouterStatusDto> CheckAsync(Customer c, bool checkSession, CancellationToken routerToken,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expected = c.PppoeDeletedAtUtc is null && c.Status != CustomerStatus.PendingApproval;
        try
        {
            var secret = await router.FindSecretAsync(c.RouterSecretId, c.PppoeUsername, routerToken);
            var session = secret is null || !checkSession ? null : await router.FindActiveSessionAsync(secret.Name, routerToken);
            var addresses = secret is not null && string.Equals(secret.Name, c.PppoeUsername, StringComparison.Ordinal)
                ? await GetAddressesAsync(secret, routerToken, cancellationToken) : null;
            return new RouterStatusDto(router.Mode, true, null, now, secret is not null, expected,
                secret is null ? null : secret.Disabled ? "Disabled" : "Enabled",
                session is not null, session?.Address, session?.Uptime, secret?.Profile, c.Plan.RouterProfile, Compare(c, secret), addresses);
        }
        catch (RouterException ex)
        {
            return Unavailable(ex.Kind);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable(RouterErrorKind.Timeout);
        }

        RouterStatusDto Unavailable(RouterErrorKind kind)
        {
            (activity ?? NullActivityLog.Instance).Write(new ActivityEvent(ActivityCategories.MikroTik, "RouterError",
                ActivityOutcomes.Failure, ActivityTargets.Customer(c.Id), $"step=StatusCheck kind={kind} mode={router.Mode}",
                ActivityLevel.Warning));
            return new RouterStatusDto(router.Mode, false, DiagnosticError(kind), now, false, expected, null, false, null, null, null, c.Plan.RouterProfile, []);
        }
    }

    private async Task<PppoeRouterAddressesDto> GetAddressesAsync(RouterSecret secret, CancellationToken routerToken,
        CancellationToken cancellationToken)
    {
        var local = AddressOrNull(secret.LocalAddress);
        var remote = AddressOrNull(secret.RemoteAddress);
        RouterPppProfile? profile = null;
        if ((local is null || remote is null) && !string.IsNullOrWhiteSpace(secret.Profile))
        {
            using var profileBudget = CancellationTokenSource.CreateLinkedTokenSource(routerToken);
            profileBudget.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                var profiles = await router.ListProfilesAsync(profileBudget.Token);
                profile = profiles.FirstOrDefault(p => p.Name == secret.Profile);
            }
            catch (RouterException)
            {
                // Keep the secret and session status when profile settings are unavailable.
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        var profileLocal = local is null ? AddressOrNull(profile?.LocalAddress) : null;
        var profileRemote = remote is null ? AddressOrNull(profile?.RemoteAddress) : null;
        return new(local ?? profileLocal, remote ?? profileRemote, profileLocal is not null, profileRemote is not null);

        static string? AddressOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static CancellationTokenSource RouterReadBudget(CancellationToken cancellationToken)
    {
        var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(12));
        return budget;
    }

    private static string DiagnosticError(RouterErrorKind kind) => kind switch
    {
        RouterErrorKind.Unreachable => "The router could not be reached.",
        RouterErrorKind.Timeout => "The router did not answer in time.",
        RouterErrorKind.Unauthorized => "The router refused API access.",
        RouterErrorKind.Certificate => "The router’s HTTPS certificate could not be verified.",
        RouterErrorKind.NotConfigured => "The router connection is not configured.",
        _ => "The router did not return the requested data.",
    };

    internal static List<RouterMismatchDto> Compare(Customer c, RouterSecret? secret)
    {
        var list = new List<RouterMismatchDto>();
        if (c.PppoeDeletedAtUtc is not null)
        {
            if (secret is not null)
                list.Add(new("PPPoE secret", "Deleted", "Exists on the router"));
            return list;
        }
        if (c.Status == CustomerStatus.PendingApproval)
        {
            if (secret is not null)
                list.Add(new("PPPoE secret", "Not created yet (waiting for approval)", "Already exists on the router"));
            return list;
        }
        if (secret is null)
        {
            list.Add(new("PPPoE secret", "Exists", "Missing on the router"));
            return list;
        }

        void Check(string field, string? expected, string? actual)
        {
            var e = string.IsNullOrWhiteSpace(expected) ? null : expected.Trim();
            var a = string.IsNullOrWhiteSpace(actual) ? null : actual.Trim();
            if (!string.Equals(e, a, StringComparison.Ordinal)) list.Add(new(field, e ?? "(not set)", a ?? "(not set)"));
        }

        Check("PPPoE Username", c.PppoeUsername, secret.Name);
        // Expected is the plan's mapped PPP profile (module 05), not the plan name.
        Check("Profile (Internet Plan)", c.Plan.RouterProfile, secret.Profile);
        var shouldBeDisabled = c.Status is CustomerStatus.Disabled or CustomerStatus.Terminated;
        if (shouldBeDisabled != secret.Disabled)
            list.Add(new("Secret state", shouldBeDisabled ? "Disabled" : "Enabled", secret.Disabled ? "Disabled" : "Enabled"));
        Check("Remote Address", c.PppoeRemoteAddress, secret.RemoteAddress);
        Check("Local Address", c.PppoeLocalAddress, secret.LocalAddress);
        return list;
    }
}
