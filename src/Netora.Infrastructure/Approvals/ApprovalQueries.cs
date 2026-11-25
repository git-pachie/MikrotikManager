using Microsoft.EntityFrameworkCore;
using Netora.Application.Approvals;
using Netora.Application.Pppoe;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Approvals;

internal sealed class ApprovalQueries(NetoraDbContext db, Netora.Application.Router.IRouterClient? router = null) : IApprovalQueries
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
                join cust in db.Customers.AsNoTracking() on req.CustomerId equals cust.Id
                where req.Id == requestId && (viewerIsManager || !cust.IsManagerOnly)
                select new { r = req, c = cust })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        var (r, c) = (row.r, row.c);
        var pending = r.Status == ApprovalStatus.Pending;
        string? blocked = null;
        if (pending)
        {
            blocked = PppoeStates.ApproveBlocker(r, c) switch
            {
                PppoeOutcome.PasswordRequired => "A PPPoE password must be set before the account can be created. Set it on the PPPoE page.",
                PppoeOutcome.NotAllowedInCurrentState => "The account has changed since this request was made, so it no longer applies. Reject it.",
                _ => null,
            };
        }

        return new ApprovalDetailDto(
            r.Id, ApprovalRequest.DisplayId(r.Id), r.Operation.ToString(), r.Status.ToString(), r.Reason,
            // While a create request waits, show what would be created now (details can still be edited).
            r.Before, pending && r.Operation == ApprovalOperation.CreatePppoe ? PppoeStates.CreateSummary(c) : r.After,
            r.TargetPlanName, r.RequestedByUserName, r.RequestedAtUtc, r.DecidedByUserName, r.DecidedAtUtc, r.DecisionNote,
            r.ProvisioningStatus.ToString(), r.ProvisioningAttempts, r.ProvisioningLastAttemptAtUtc, r.ProvisioningError,
            c.Id, Customer.DisplayId(c.Id), Name(c.FirstName, c.LastName), c.Status.ToString(), PppoeStates.AccountState(c.Status),
            c.PppoeUsername, c.PlanName, c.PppoePasswordProtected != null, c.IsManagerOnly,
            CanDecide: viewerIsManager && pending,
            ApproveBlockedReason: blocked,
            RouterMode: router?.Mode ?? "Mock",
            CanRetry: viewerIsManager && r.Status == ApprovalStatus.Approved
                      && r.ProvisioningStatus is ProvisioningStatus.NotSent or ProvisioningStatus.Failed);
    }

    private static string Name(string first, string last) => string.IsNullOrWhiteSpace(last) ? first : $"{first} {last}";
}
