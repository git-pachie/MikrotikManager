using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Netora.Application.Customers;
using Netora.Application.Customers.Attachments;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Customers;

internal sealed class CustomerQueries(NetoraDbContext db, IOptions<AttachmentOptions> attachmentOptions) : ICustomerQueries
{
    public async Task<CustomerListDto> ListAsync(CustomerListQuery query, bool includeManagerOnly, CancellationToken cancellationToken)
    {
        // Visibility is applied first, so every count, option list and search below only sees allowed rows.
        var visible = db.Customers.AsNoTracking();
        if (!includeManagerOnly)
            visible = visible.Where(c => !c.IsManagerOnly);

        var stats = await visible
            .GroupBy(_ => 1)
            .Select(g => new CustomerStatsDto(
                g.Count(),
                g.Count(c => c.Status == CustomerStatus.Active),
                g.Count(c => c.Status == CustomerStatus.Disabled),
                g.Count(c => c.Status == CustomerStatus.Terminated),
                g.Count(c => c.Nap.Code == Customer.UnknownNap)))
            .FirstOrDefaultAsync(cancellationToken) ?? new CustomerStatsDto(0, 0, 0, 0, 0);

        var napOptions = await visible.Select(c => c.Nap.Code).Distinct().OrderBy(n => n).ToListAsync(cancellationToken);
        var planOptions = await visible.Select(c => c.Plan.Name).Distinct().OrderBy(p => p).ToListAsync(cancellationToken);
        var containsSample = await visible.AnyAsync(c => c.IsSampleData, cancellationToken);

        var filtered = visible;
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
        {
            filtered = filtered.Where(c =>
                c.FirstName.Contains(search) || c.LastName.Contains(search) ||
                c.PppoeUsername.Contains(search) ||
                (c.MobileNumber != null && c.MobileNumber.Contains(search)) ||
                (c.Address != null && c.Address.Contains(search)));
        }
        if (Enum.TryParse<CustomerStatus>(query.Status, ignoreCase: true, out var status) && Enum.IsDefined(status))
            filtered = filtered.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Nap) && !IsAll(query.Nap))
            filtered = filtered.Where(c => c.Nap.Code == query.Nap);
        if (!string.IsNullOrWhiteSpace(query.Plan) && !IsAll(query.Plan))
            filtered = filtered.Where(c => c.Plan.Name == query.Plan);

        var pageSize = CustomerListQuery.AllowedPageSizes.Contains(query.PageSize) ? query.PageSize : 10;
        var totalMatching = await filtered.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalMatching / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        var rows = await ApplySort(filtered, query)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id, c.FirstName, c.LastName, c.PppoeUsername, PlanName = c.Plan.Name, NapBox = c.Nap.Code, c.OnuSerial, c.Status, c.CreatedAtUtc,
                c.PppoeDeletionRequestedAtUtc, c.PppoeDeletedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(c => new CustomerListItemDto(
                c.Id, $"{c.LastName}, {c.FirstName}", c.PppoeUsername, c.PlanName, c.NapBox, c.OnuSerial,
                c.Status.ToString(), c.CreatedAtUtc,
                includeManagerOnly && c.PppoeDeletionRequestedAtUtc is null && c.PppoeDeletedAtUtc is null,
                c.PppoeDeletedAtUtc is not null))
            .ToList();

        return new CustomerListDto(items, totalMatching, page, pageSize, stats, napOptions, planOptions, containsSample);
    }

    public async Task<CustomerFormOptionsDto> GetFormOptionsAsync(bool includeManagerOnly, CancellationToken cancellationToken)
    {
        // Active catalogue entries only (05, 06). The catalogues hold no customer data, so Staff and Managers see the
        // same lists; the edit pages add a customer's current plan or NAP if it has been deactivated since.
        var plans = await db.InternetPlans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).Select(p => p.Name)
            .ToListAsync(cancellationToken);
        var naps = await db.NapBoxes.AsNoTracking().Where(n => n.IsActive && !n.IsSystem).OrderBy(n => n.Code).Select(n => n.Code)
            .ToListAsync(cancellationToken);
        naps.Insert(0, Customer.UnknownNap);
        return new CustomerFormOptionsDto(plans, naps);
    }

    public async Task<CustomerDetailDto?> GetDetailAsync(int id, bool viewerIsManager, CancellationToken cancellationToken)
    {
        // Same visibility rule as the list: a hidden customer is indistinguishable from a missing one.
        var customer = await db.Customers.AsNoTracking().Include(c => c.Plan).Include(c => c.Nap)
            .FirstOrDefaultAsync(c => c.Id == id && (viewerIsManager || !c.IsManagerOnly), cancellationToken);
        if (customer is null) return null;

        var activity = await db.CustomerActivities.AsNoTracking()
            .Where(a => a.CustomerId == id)
            .OrderByDescending(a => a.OccurredAtUtc).ThenByDescending(a => a.Id)
            .Take(50)
            .Select(a => new CustomerActivityDto(a.OccurredAtUtc, a.Action, a.Detail, a.ActorUserName, a.Outcome))
            .ToListAsync(cancellationToken);

        // Deleted attachments are hidden here but kept in the table and on disk (retention).
        var attachmentRows = await db.CustomerAttachments.AsNoTracking()
            .Where(a => a.CustomerId == id && !a.IsDeleted)
            .OrderByDescending(a => a.UploadedAtUtc).ThenByDescending(a => a.Id)
            .ToListAsync(cancellationToken);
        var attachments = attachmentRows.Select(AttachmentFileTypes.ToDto).ToList();

        return new CustomerDetailDto(
            customer.Id, Customer.DisplayId(customer.Id), customer.FirstName, customer.LastName, customer.MobileNumber,
            customer.Email, customer.Address, customer.Notes, customer.PppoeUsername, customer.Plan.Name, customer.Nap.Code,
            customer.OnuSerial, customer.OnuModel, customer.Status.ToString(), customer.IsManagerOnly,
            customer.CreatedAtUtc, customer.UpdatedAtUtc, customer.ConcurrencyStamp,
            customer.CanBeChangedBy(viewerIsManager), customer.ServiceFieldsLocked, activity, attachments,
            attachmentOptions.Value.MaxFileSizeBytes, customer.PppoeDeletedAtUtc is not null,
            viewerIsManager && customer.PppoeDeletionRequestedAtUtc is null && customer.PppoeDeletedAtUtc is null,
            customer.PppoeLocalAddress, customer.PppoeRemoteAddress,
            customer.PppoeDeletedAtUtc is null && !string.IsNullOrEmpty(customer.PppoePasswordProtected));
    }

    public Task<bool> IsManagerOnlyAsync(int id, CancellationToken cancellationToken)
        => db.Customers.AsNoTracking().AnyAsync(c => c.Id == id && c.IsManagerOnly, cancellationToken);

    /// <summary>
    /// Sorts by a whitelisted column. The Id tie-breaker keeps paging stable when many rows share a value.
    /// Default (no or unknown column) is the original order: by ID, which is the order of creation.
    /// </summary>
    private static IOrderedQueryable<Customer> ApplySort(IQueryable<Customer> source, CustomerListQuery query)
    {
        var desc = query.Descending;
        var column = query.SortBy?.Trim().ToLowerInvariant();
        return column switch
        {
            "name" => desc
                ? source.OrderByDescending(c => c.LastName).ThenByDescending(c => c.FirstName).ThenBy(c => c.Id)
                : source.OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ThenBy(c => c.Id),
            "username" => desc ? source.OrderByDescending(c => c.PppoeUsername).ThenBy(c => c.Id) : source.OrderBy(c => c.PppoeUsername).ThenBy(c => c.Id),
            "plan" => desc ? source.OrderByDescending(c => c.Plan.Name).ThenBy(c => c.Id) : source.OrderBy(c => c.Plan.Name).ThenBy(c => c.Id),
            "nap" => desc ? source.OrderByDescending(c => c.Nap.Code).ThenBy(c => c.Id) : source.OrderBy(c => c.Nap.Code).ThenBy(c => c.Id),
            "onu" => desc ? source.OrderByDescending(c => c.OnuSerial).ThenBy(c => c.Id) : source.OrderBy(c => c.OnuSerial).ThenBy(c => c.Id),
            "status" => desc ? source.OrderByDescending(c => c.Status).ThenBy(c => c.Id) : source.OrderBy(c => c.Status).ThenBy(c => c.Id),
            "created" => desc ? source.OrderByDescending(c => c.CreatedAtUtc).ThenBy(c => c.Id) : source.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id),
            _ => source.OrderBy(c => c.Id),
        };
    }

    private static bool IsAll(string value) => string.Equals(value, "All", StringComparison.OrdinalIgnoreCase);
}
