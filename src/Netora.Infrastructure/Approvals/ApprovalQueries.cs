using Microsoft.EntityFrameworkCore;
using Netora.Application.Approvals;
using Netora.Application.Pppoe;
using Netora.Application.Router;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Approvals;

internal sealed class ApprovalQueries(NetoraDbContext db, IRouterClient? router = null) : IApprovalQueries
{
    public async Task<ApprovalListDto> ListAsync(ApprovalListQuery query, bool viewerIsManager, CancellationToken cancellationToken)
    {
        // Visibility first: requests for Manager-only customers are invisible to Staff, also in the counts.
        var visible =
            from r in db.ApprovalRequests.AsNoTracking()
            join c in db.Customers.AsNoTracking() on r.CustomerId equals c.Id
            where viewerIsManager || !c.IsManagerOnly
            select new { r, c };

        var counts = new ApprovalCountsDto(
            await visible.CountAsync(x => x.r.Status == ApprovalStatus.Pending, cancellationToken),
            await visible.CountAsync(x => x.r.Status == ApprovalStatus.Approved, cancellationToken),
            await visible.CountAsync(x => x.r.Status == ApprovalStatus.Rejected, cancellationToken));

        var filtered = query.Status?.Trim().ToLowerInvariant() switch
        {
            "approved" => visible.Where(x => x.r.Status == ApprovalStatus.Approved),
            "rejected" => visible.Where(x => x.r.Status == ApprovalStatus.Rejected),
            "all" => visible,
            _ => visible.Where(x => x.r.Status == ApprovalStatus.Pending),
        };

        if (PppoeStates.TryParseOperation(query.Operation, out var operation))
            filtered = filtered.Where(x => x.r.Operation == operation);

        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            filtered = filtered.Where(x => x.c.PppoeUsername.Contains(search) || x.c.FirstName.Contains(search)
                                           || x.c.LastName.Contains(search) || x.r.RequestedByUserName.Contains(search));
        }

        var pageSize = ApprovalListQuery.AllowedPageSizes.Contains(query.PageSize) ? query.PageSize : 10;
        var total = await filtered.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        // Pending: oldest first, so the longest-waiting request is reviewed first. Decided: newest first.
        var ordered = string.Equals(query.Status, "pending", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(query.Status)
            ? filtered.OrderBy(x => x.r.RequestedAtUtc).ThenBy(x => x.r.Id)
            : filtered.OrderByDescending(x => x.r.RequestedAtUtc).ThenByDescending(x => x.r.Id);

        var rows = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.r.Id, x.r.CustomerId, x.c.FirstName, x.c.LastName, x.c.PppoeUsername, x.r.Operation, x.r.Status,
                x.r.RequestedByUserName, x.r.RequestedAtUtc, x.r.DecidedByUserName, x.r.DecidedAtUtc, x.r.ProvisioningStatus,
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(x => new ApprovalListItemDto(
            x.Id, ApprovalRequest.DisplayId(x.Id), x.CustomerId, Customer.DisplayId(x.CustomerId), Name(x.FirstName, x.LastName),
            x.PppoeUsername, x.Operation.ToString(), x.Status.ToString(), x.RequestedByUserName, x.RequestedAtUtc,
            x.DecidedByUserName, x.DecidedAtUtc, x.ProvisioningStatus.ToString())).ToList();

        return new ApprovalListDto(items, total, page, pageSize, counts);
    }

    public async Task<ApprovalDetailDto?> GetAsync(long requestId, bool viewerIsManager, CancellationToken cancellationToken)
    {
        var row = await (
                from req in db.ApprovalRequests.AsNoTracking()
                join cust in db.Customers.IgnoreQueryFilters().AsNoTracking() on req.CustomerId equals cust.Id
                join plan in db.InternetPlans.AsNoTracking() on cust.PlanId equals plan.Id
                where req.Id == requestId && (viewerIsManager || (!cust.IsManagerOnly && cust.DeletedAtUtc == null))
                select new { r = req, c = cust, plan })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var (r, c) = (row.r, row.c);
        c.Plan = row.plan;
        var pending = r.Status == ApprovalStatus.Pending;
        string? blocked = null;
        if (pending)
        {
            blocked = PppoeStates.ApproveBlocker(r, c) switch
            {
                PppoeOutcome.NotAllowedInCurrentState => "The account has changed since this request was made, so it no longer applies. Reject it.",
                PppoeOutcome.DeletionInProgress => "The account is being deleted. Reject this request.",
                _ => null,
            };
        }

        var canDecide = viewerIsManager && pending && c.DeletedAtUtc is null;
        var creation = canDecide && blocked is null && r.Operation == ApprovalOperation.CreatePppoe
            ? await CreationOptionsAsync(c, cancellationToken) : null;

        return new ApprovalDetailDto(
            r.Id, ApprovalRequest.DisplayId(r.Id), r.Operation.ToString(), r.Status.ToString(), r.Reason,
            // While a create request waits, show what would be created now (details can still be edited).
            r.Before, pending && r.Operation == ApprovalOperation.CreatePppoe ? PppoeStates.CreateSummary(c) : r.After,
            r.TargetPlanName, r.RequestedByUserName, r.RequestedAtUtc, r.DecidedByUserName, r.DecidedAtUtc, r.DecisionNote,
            r.ProvisioningStatus.ToString(), r.ProvisioningAttempts, r.ProvisioningLastAttemptAtUtc, r.ProvisioningError,
            c.Id, Customer.DisplayId(c.Id), Name(c.FirstName, c.LastName), c.Status.ToString(),
            c.PppoeDeletedAtUtc is null ? PppoeStates.AccountState(c.Status) : "Deleted",
            c.PppoeUsername, c.Plan.Name, c.PppoePasswordProtected != null, c.IsManagerOnly,
            CanDecide: canDecide,
            ApproveBlockedReason: blocked,
            RouterMode: router?.Mode ?? "Mock",
            CanRetry: viewerIsManager && c.DeletedAtUtc is null
                      && (r.Operation == ApprovalOperation.DeletePppoe || c.PppoeDeletionRequestedAtUtc is null)
                      && r.Status == ApprovalStatus.Approved
                      && r.ProvisioningStatus is ProvisioningStatus.NotSent or ProvisioningStatus.Failed,
            CustomerDeleted: c.DeletedAtUtc is not null,
            Creation: creation);
    }

    private async Task<CreatePppoeApprovalOptions> CreationOptionsAsync(Customer customer, CancellationToken cancellationToken)
    {
        var plans = await db.InternetPlans.AsNoTracking().Where(p => p.IsActive || p.Id == customer.PlanId)
            .OrderBy(p => p.Name).Select(p => new { p.Id, p.Name, p.RouterProfile }).ToListAsync(cancellationToken);
        var naps = await db.NapBoxes.AsNoTracking().Where(n => n.IsActive || n.Id == customer.NapBoxId)
            .OrderBy(n => n.Code).Select(n => new ApprovalNapOption(n.Code, n.Area)).ToListAsync(cancellationToken);
        IReadOnlyList<RouterPppProfile> routerProfiles = [];
        if (router is not null)
        {
            try
            {
                routerProfiles = await router.ListProfilesAsync(cancellationToken);
            }
            catch (RouterException)
            {
                // A blank local address still inherits from the profile when router provisioning runs.
            }
        }
        var profiles = plans.Select(p => new ApprovalProfileOption(p.Id, p.Name, p.RouterProfile,
            routerProfiles.FirstOrDefault(r => r.Name == p.RouterProfile)?.LocalAddress)).ToList();
        var napCode = await db.NapBoxes.Where(n => n.Id == customer.NapBoxId)
            .Select(n => n.Code).SingleAsync(cancellationToken);
        return new(customer.PlanId, napCode, customer.PppoeLocalAddress, customer.PppoeRemoteAddress,
            customer.ConcurrencyStamp, profiles, naps);
    }

    private static string Name(string first, string last) => string.IsNullOrWhiteSpace(last) ? first : $"{first} {last}";
}
