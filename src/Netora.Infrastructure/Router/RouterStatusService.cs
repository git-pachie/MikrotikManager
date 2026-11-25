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
    public async Task<RouterStatusDto?> CheckAsync(int customerId, bool viewerIsManager, CancellationToken cancellationToken)
    {
        var c = await db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == customerId && (viewerIsManager || !x.IsManagerOnly), cancellationToken);
        if (c is null) return null;

        var now = DateTimeOffset.UtcNow;
        var expected = c.Status != CustomerStatus.PendingApproval;
        try
        {
            var secret = await router.FindSecretAsync(c.RouterSecretId, c.PppoeUsername, cancellationToken);
            var session = secret is null || secret.Disabled ? null : await router.FindActiveSessionAsync(secret.Name, cancellationToken);
            return new RouterStatusDto(router.Mode, true, null, now, secret is not null, expected,
                secret is null ? null : secret.Disabled ? "Disabled" : "Enabled",
                session is not null, session?.Address, session?.Uptime, secret?.Profile, c.PlanName, Compare(c, secret));
        }
        catch (RouterException ex)
        {
            (activity ?? NullActivityLog.Instance).Write(new ActivityEvent(ActivityCategories.MikroTik, "RouterError",
                ActivityOutcomes.Failure, ActivityTargets.Customer(c.Id), $"step=StatusCheck kind={ex.Kind} mode={router.Mode}",
                ActivityLevel.Warning));
            return new RouterStatusDto(router.Mode, false, ex.Message, now, false, expected, null, false, null, null, null, c.PlanName, []);
        }
    }

    internal static List<RouterMismatchDto> Compare(Customer c, RouterSecret? secret)
    {
        var list = new List<RouterMismatchDto>();
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
        Check("Profile (Internet Plan)", c.PlanName, secret.Profile);
        var shouldBeDisabled = c.Status is CustomerStatus.Disabled or CustomerStatus.Terminated;
        if (shouldBeDisabled != secret.Disabled)
            list.Add(new("Secret state", shouldBeDisabled ? "Disabled" : "Enabled", secret.Disabled ? "Disabled" : "Enabled"));
        Check("Remote Address", c.PppoeRemoteAddress, secret.RemoteAddress);
        Check("Local Address", c.PppoeLocalAddress, secret.LocalAddress);
        return list;
    }
}
