using Microsoft.EntityFrameworkCore;
using Netora.Application.Customers;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Customers;

internal sealed class CustomerQueries(NetoraDbContext db) : ICustomerQueries
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
                g.Count(c => c.NapBox == Customer.UnknownNap)))
            .FirstOrDefaultAsync(cancellationToken) ?? new CustomerStatsDto(0, 0, 0, 0, 0);

        var napOptions = await visible.Select(c => c.NapBox).Distinct().OrderBy(n => n).ToListAsync(cancellationToken);
        var planOptions = await visible.Select(c => c.PlanName).Distinct().OrderBy(p => p).ToListAsync(cancellationToken);
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
            filtered = filtered.Where(c => c.NapBox == query.Nap);
        if (!string.IsNullOrWhiteSpace(query.Plan) && !IsAll(query.Plan))
            filtered = filtered.Where(c => c.PlanName == query.Plan);

        var pageSize = CustomerListQuery.AllowedPageSizes.Contains(query.PageSize) ? query.PageSize : 10;
        var totalMatching = await filtered.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalMatching / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);

        var rows = await filtered
            .OrderBy(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new
            {
                c.Id, c.FirstName, c.LastName, c.PppoeUsername, c.PlanName, c.NapBox, c.OnuSerial, c.Status, c.CreatedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(c => new CustomerListItemDto(
                c.Id, $"{c.LastName}, {c.FirstName}", c.PppoeUsername, c.PlanName, c.NapBox, c.OnuSerial,
                c.Status.ToString(), c.CreatedAtUtc))
            .ToList();

        return new CustomerListDto(items, totalMatching, page, pageSize, stats, napOptions, planOptions, containsSample);
    }

    public async Task<CustomerFormOptionsDto> GetFormOptionsAsync(bool includeManagerOnly, CancellationToken cancellationToken)
    {
        var visible = db.Customers.AsNoTracking();
        if (!includeManagerOnly)
            visible = visible.Where(c => !c.IsManagerOnly);

        var plans = await visible.Select(c => c.PlanName).Distinct().OrderBy(p => p).ToListAsync(cancellationToken);
        var naps = await visible.Select(c => c.NapBox).Where(n => n != Customer.UnknownNap).Distinct().OrderBy(n => n)
            .ToListAsync(cancellationToken);
        naps.Insert(0, Customer.UnknownNap);
        return new CustomerFormOptionsDto(plans, naps);
    }

    private static bool IsAll(string value) => string.Equals(value, "All", StringComparison.OrdinalIgnoreCase);
}
