using Microsoft.EntityFrameworkCore;
using Netora.Application.Pppoe;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Pppoe;

internal sealed class PppoeQueries(NetoraDbContext db) : IPppoeQueries
{
    public async Task<PppoeListDto> ListAsync(PppoeListQuery query, bool viewerIsManager, CancellationToken cancellationToken)
    {
        // Visibility first: an account linked to a Manager-only customer is invisible to Staff, also in the counts.
        var visible = db.Customers.AsNoTracking().Where(c => c.PppoeDeletedAtUtc == null);
        if (!viewerIsManager) visible = visible.Where(c => !c.IsManagerOnly);

        var pending = db.ApprovalRequests.AsNoTracking().Where(r => r.Status == ApprovalStatus.Pending);

        var stats = new PppoeStatsDto(
            await visible.CountAsync(cancellationToken),
            await visible.CountAsync(c => c.Status == CustomerStatus.Active, cancellationToken),
            await visible.CountAsync(c => c.Status == CustomerStatus.Disabled, cancellationToken),
            await visible.CountAsync(c => c.Status == CustomerStatus.PendingApproval, cancellationToken),
            await visible.CountAsync(c => pending.Any(r => r.CustomerId == c.Id), cancellationToken));

        var filtered = visible;
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            filtered = filtered.Where(c => c.PppoeUsername.Contains(search) || c.FirstName.Contains(search)
                                           || c.LastName.Contains(search) || c.Plan.Name.Contains(search));
        }

        filtered = query.State?.Trim().ToLowerInvariant() switch
        {
            "enabled" => filtered.Where(c => c.Status == CustomerStatus.Active),
            "disabled" => filtered.Where(c => c.Status == CustomerStatus.Disabled),
            "notcreated" => filtered.Where(c => c.Status == CustomerStatus.PendingApproval),
            "terminated" => filtered.Where(c => c.Status == CustomerStatus.Terminated),
            "requested" => filtered.Where(c => pending.Any(r => r.CustomerId == c.Id)),
            _ => filtered,
        };

        var pageSize = PppoeListQuery.AllowedPageSizes.Contains(query.PageSize) ? query.PageSize : 10;
        var total = await filtered.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        var rows = await Sort(filtered, query, pending)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id, c.FirstName, c.LastName, c.PppoeUsername, PlanName = c.Plan.Name, c.Status, c.RouterSyncStatus,
                PasswordSet = c.PppoePasswordProtected != null,
                DeletePending = c.PppoeDeletionRequestedAtUtc != null,
                Pending = pending.Where(r => r.CustomerId == c.Id).Select(r => (ApprovalOperation?)r.Operation).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new PppoeListItemDto(
            r.Id, Customer.DisplayId(r.Id), Name(r.FirstName, r.LastName), r.PppoeUsername, r.PlanName,
            PppoeStates.AccountState(r.Status), r.RouterSyncStatus.ToString(), r.PasswordSet, r.Pending?.ToString(),
            viewerIsManager && !r.DeletePending)).ToList();

        return new PppoeListDto(items, total, page, pageSize, stats);
    }

    public async Task<PppoeDetailDto?> GetAsync(int customerId, bool viewerIsManager, CancellationToken cancellationToken)
    {
        var c = await db.Customers.AsNoTracking().Include(x => x.Plan)
            .FirstOrDefaultAsync(x => x.Id == customerId && x.PppoeDeletedAtUtc == null && (viewerIsManager || !x.IsManagerOnly), cancellationToken);
        if (c is null) return null;

        var requests = await db.ApprovalRequests.AsNoTracking()
            .Where(r => r.CustomerId == customerId)
            .OrderByDescending(r => r.RequestedAtUtc).ThenByDescending(r => r.Id)
            .Take(10)
            .ToListAsync(cancellationToken);
        var pending = requests.FirstOrDefault(r => r.Status == ApprovalStatus.Pending);
        var canChange = c.PppoeDeletionRequestedAtUtc is null && c.CanBeChangedBy(viewerIsManager);

        return new PppoeDetailDto(
            c.Id, Customer.DisplayId(c.Id), Name(c.FirstName, c.LastName), c.Status.ToString(), PppoeStates.AccountState(c.Status),
            c.PppoeUsername, c.Plan.Name, c.PppoePasswordProtected != null, c.PppoeRemoteAddress, c.PppoeLocalAddress,
            c.PppoeComment, c.RouterSyncStatus.ToString(), c.ConcurrencyStamp,
            CanEdit: c.PppoeDetailsEditableBy(viewerIsManager),
            ServiceFieldsLocked: c.ServiceFieldsLocked,
            CanRequestDisable: canChange && pending is null && PppoeStates.Fits(ApprovalOperation.DisablePppoe, c.Status),
            CanRequestEnable: canChange && pending is null && PppoeStates.Fits(ApprovalOperation.EnablePppoe, c.Status),
            PendingRequest: pending is null ? null : PppoeStates.ToDto(pending),
            RecentRequests: requests.Select(PppoeStates.ToDto).ToList(),
            CanRequestPlanChange: canChange && pending is null && PppoeStates.Fits(ApprovalOperation.ChangePlan, c.Status),
            CanRequestTerminate: canChange && pending is null && PppoeStates.Fits(ApprovalOperation.TerminatePppoe, c.Status),
            // A rejected creation can be sent for approval again.
            CanResubmitCreate: canChange && pending is null && PppoeStates.Fits(ApprovalOperation.CreatePppoe, c.Status),
            CanDelete: viewerIsManager && canChange,
            DeletionRequestId: requests.FirstOrDefault(r => r.Operation == ApprovalOperation.DeletePppoe)?.Id);
    }

    private static IOrderedQueryable<Customer> Sort(IQueryable<Customer> source, PppoeListQuery query, IQueryable<ApprovalRequest> pending)
    {
        var desc = query.Descending;
        return query.SortBy?.Trim().ToLowerInvariant() switch
        {
            "username" => desc ? source.OrderByDescending(c => c.PppoeUsername).ThenBy(c => c.Id) : source.OrderBy(c => c.PppoeUsername).ThenBy(c => c.Id),
            "customer" => desc
                ? source.OrderByDescending(c => c.LastName).ThenByDescending(c => c.FirstName).ThenBy(c => c.Id)
                : source.OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ThenBy(c => c.Id),
            "plan" => desc ? source.OrderByDescending(c => c.Plan.Name).ThenBy(c => c.Id) : source.OrderBy(c => c.Plan.Name).ThenBy(c => c.Id),
            "state" => desc ? source.OrderByDescending(c => c.Status).ThenBy(c => c.Id) : source.OrderBy(c => c.Status).ThenBy(c => c.Id),
            "requested" => desc
                ? source.OrderByDescending(c => pending.Any(r => r.CustomerId == c.Id)).ThenBy(c => c.Id)
                : source.OrderBy(c => pending.Any(r => r.CustomerId == c.Id)).ThenBy(c => c.Id),
            _ => source.OrderBy(c => c.PppoeUsername).ThenBy(c => c.Id),
        };
    }

    private static string Name(string first, string last) => string.IsNullOrWhiteSpace(last) ? first : $"{first} {last}";
}
