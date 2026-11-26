using Microsoft.EntityFrameworkCore;
using Netora.Application.Audit;
using Netora.Domain.Audit;
using Netora.Domain.Security;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Audit;

/// <summary>
/// The Audit Trail (12.01) as one list over the records Netora already keeps: SecurityEvents (sign-in, users and
/// settings), CustomerActivities (customers, PPPoE, approval steps and router results) and AuditEvents (plans and NAP
/// boxes). Each source is filtered and sorted in SQL, the newest rows of each are merged, and one page is returned.
/// Managers only; nothing here is ever changed or deleted.
/// </summary>
internal sealed class AuditQueries(NetoraDbContext db) : IAuditQueries
{
    public async Task<AuditListDto> ListAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        var pageSize = AuditQuery.AllowedPageSizes.Contains(query.PageSize) ? query.PageSize : 25;
        var page = Math.Clamp(query.Page, 1, AuditQuery.MaxPage);
        var take = page * pageSize;
        var (from, to) = Range(query.From, query.To);
        var category = AuditCategories.All.FirstOrDefault(c => string.Equals(c, query.Category?.Trim(), StringComparison.OrdinalIgnoreCase));
        var actor = Clean(query.Actor);
        var search = Clean(query.Search);

        var rows = new List<AuditEntryDto>();
        var total = 0;

        if (category is null or AuditCategories.SignIn or AuditCategories.UsersAndSettings)
        {
            var (count, items) = await SecurityAsync(category, from, to, actor, search, take, cancellationToken);
            total += count;
            rows.AddRange(items);
        }
        if (category is null or AuditCategories.Customers or AuditCategories.Pppoe or AuditCategories.Approvals or AuditCategories.Router)
        {
            var (count, items) = await ActivitiesAsync(category, from, to, actor, search, take, cancellationToken);
            total += count;
            rows.AddRange(items);
        }
        if (category is null or AuditCategories.PlansAndNap)
        {
            var (count, items) = await CatalogAsync(from, to, actor, search, take, cancellationToken);
            total += count;
            rows.AddRange(items);
        }

        var pageItems = rows.OrderByDescending(r => r.OccurredAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new AuditListDto(pageItems, total, page, pageSize);
    }

    private async Task<(int, List<AuditEntryDto>)> SecurityAsync(string? category, DateTimeOffset? from, DateTimeOffset? to,
        string? actor, string? search, int take, CancellationToken ct)
    {
        var signIn = AuditLabels.SignInEvents.ToList();
        var q = from e in db.SecurityEvents.AsNoTracking()
                join u in db.Users.AsNoTracking() on e.UserId equals u.Id into users
                from u in users.DefaultIfEmpty()
                select new { e, ActorName = u == null ? null : u.UserName };

        if (category == AuditCategories.SignIn) q = q.Where(x => signIn.Contains(x.e.EventType));
        else if (category == AuditCategories.UsersAndSettings) q = q.Where(x => !signIn.Contains(x.e.EventType));
        if (from is not null) q = q.Where(x => x.e.TimestampUtc >= from);
        if (to is not null) q = q.Where(x => x.e.TimestampUtc < to);
        if (actor is not null)
        {
            var by = "by " + actor;
            // Sign-in rows name the user; administration rows end with ", by {actor}"; settings rows link the actor's account.
            q = q.Where(x => (x.e.UserName != null && x.e.UserName.Contains(actor) && signIn.Contains(x.e.EventType))
                             || (x.e.Reason != null && x.e.Reason.Contains(by))
                             || (x.e.EventType == SecurityEventTypes.SettingsUpdated && x.ActorName != null && x.ActorName.Contains(actor)));
        }
        if (search is not null)
            q = q.Where(x => x.e.EventType.Contains(search) || (x.e.UserName != null && x.e.UserName.Contains(search))
                             || (x.e.Reason != null && x.e.Reason.Contains(search)) || (x.e.IpAddress != null && x.e.IpAddress.Contains(search)));

        var count = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.e.TimestampUtc).ThenByDescending(x => x.e.Id).Take(take).ToListAsync(ct);
        return (count, rows.Select(x => Security(x.e, x.ActorName)).ToList());
    }

    internal static AuditEntryDto Security(SecurityEvent e, string? linkedUserName)
    {
        var isSignIn = AuditLabels.SignInEvents.Contains(e.EventType);
        var result = e.Succeeded ? "Success" : "Failed";
        if (isSignIn)
        {
            var detail = string.Join(", ", new[] { e.Reason, e.IpAddress is null ? null : "IP " + e.IpAddress }.Where(s => !string.IsNullOrEmpty(s)));
            return new AuditEntryDto(e.TimestampUtc, e.UserName ?? "anonymous", AuditCategories.SignIn,
                e.UserName is null ? "Unknown user" : $"User {e.UserName}", UserLink(e.UserId), AuditLabels.Action(e.EventType), result,
                detail.Length == 0 ? null : detail);
        }
        if (e.EventType == SecurityEventTypes.SettingsUpdated)
            return new AuditEntryDto(e.TimestampUtc, linkedUserName ?? "unknown", AuditCategories.UsersAndSettings, "Settings", "/settings",
                AuditLabels.Action(e.EventType), result, e.Reason);

        var (by, rest) = AuditLabels.SplitBy(e.Reason);
        return new AuditEntryDto(e.TimestampUtc, by ?? (e.EventType == SecurityEventTypes.ManagerBootstrapped ? "system" : "unknown"),
            AuditCategories.UsersAndSettings, $"User {e.UserName}", UserLink(e.UserId), AuditLabels.Action(e.EventType), result, rest);
    }

    private async Task<(int, List<AuditEntryDto>)> ActivitiesAsync(string? category, DateTimeOffset? from, DateTimeOffset? to,
        string? actor, string? search, int take, CancellationToken ct)
    {
        var customerActions = AuditLabels.CustomerActions.ToList();
        var pppoeActions = AuditLabels.PppoeActions.ToList();
        var routerActions = AuditLabels.RouterActions.ToList();

        var q = from a in db.CustomerActivities.AsNoTracking()
                join c in db.Customers.AsNoTracking() on a.CustomerId equals c.Id
                select new { a, c.FirstName, c.LastName, c.PppoeUsername };

        q = category switch
        {
            AuditCategories.Customers => q.Where(x => customerActions.Contains(x.a.Action)),
            AuditCategories.Pppoe => q.Where(x => pppoeActions.Contains(x.a.Action)),
            AuditCategories.Router => q.Where(x => routerActions.Contains(x.a.Action)),
            AuditCategories.Approvals => q.Where(x => x.a.Action.StartsWith("Pppoe")
                && (x.a.Action.EndsWith("Requested") || x.a.Action.EndsWith("Approved") || x.a.Action.EndsWith("Rejected"))),
            _ => q,
        };
        if (from is not null) q = q.Where(x => x.a.OccurredAtUtc >= from);
        if (to is not null) q = q.Where(x => x.a.OccurredAtUtc < to);
        if (actor is not null) q = q.Where(x => x.a.ActorUserName.Contains(actor));
        if (search is not null)
            q = q.Where(x => x.a.Action.Contains(search) || (x.a.Detail != null && x.a.Detail.Contains(search))
                             || x.FirstName.Contains(search) || x.LastName.Contains(search) || x.PppoeUsername.Contains(search));

        var count = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.a.OccurredAtUtc).ThenByDescending(x => x.a.Id).Take(take).ToListAsync(ct);
        return (count, rows.Select(x => new AuditEntryDto(
            x.a.OccurredAtUtc, x.a.ActorUserName, ActivityCategory(x.a.Action),
            string.IsNullOrWhiteSpace(x.LastName) ? x.FirstName : $"{x.FirstName} {x.LastName}", $"/customers/{x.a.CustomerId}",
            AuditLabels.Action(x.a.Action), x.a.Outcome, x.a.Detail)).ToList());
    }

    internal static string ActivityCategory(string action)
    {
        if (AuditLabels.PppoeActions.Contains(action)) return AuditCategories.Pppoe;
        if (AuditLabels.RouterActions.Contains(action)) return AuditCategories.Router;
        if (AuditLabels.ApprovalStep(action) is not null) return AuditCategories.Approvals;
        return AuditCategories.Customers;
    }

    private async Task<(int, List<AuditEntryDto>)> CatalogAsync(DateTimeOffset? from, DateTimeOffset? to, string? actor,
        string? search, int take, CancellationToken ct)
    {
        var q = db.AuditEvents.AsNoTracking();
        if (from is not null) q = q.Where(a => a.OccurredAtUtc >= from);
        if (to is not null) q = q.Where(a => a.OccurredAtUtc < to);
        if (actor is not null) q = q.Where(a => a.ActorUserName.Contains(actor));
        if (search is not null)
            q = q.Where(a => a.EntityName.Contains(search) || a.Action.Contains(search) || (a.Detail != null && a.Detail.Contains(search)));

        var count = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id).Take(take).ToListAsync(ct);
        return (count, rows.Select(a => new AuditEntryDto(
            a.OccurredAtUtc, a.ActorUserName, AuditCategories.PlansAndNap,
            a.EntityType == AuditEntityTypes.Plan ? $"Plan {a.EntityName}" : $"NAP {a.EntityName}",
            a.EntityType == AuditEntityTypes.Plan ? $"/plans/{a.EntityId}/edit" : $"/nap-boxes/{a.EntityId}",
            AuditLabels.Action(a.Action), a.Outcome, a.Detail)).ToList());
    }

    /// <summary>Local calendar days to a UTC range: from the start of From to the end of To.</summary>
    internal static (DateTimeOffset? From, DateTimeOffset? To) Range(DateOnly? from, DateOnly? to)
    {
        static DateTimeOffset StartOf(DateOnly d)
        {
            var local = d.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
        }
        return (from is null ? null : StartOf(from.Value), to is null ? null : StartOf(to.Value.AddDays(1)));
    }

    private static string? UserLink(string? userId) => string.IsNullOrEmpty(userId) ? null : $"/users/{Uri.EscapeDataString(userId)}/edit";

    private static string? Clean(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        return v.Length > 100 ? v[..100] : v;
    }
}
