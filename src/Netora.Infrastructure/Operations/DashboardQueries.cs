using Microsoft.EntityFrameworkCore;
using Netora.Application.Operations;
using Netora.Application.Router;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Operations;

internal sealed class DashboardQueries(NetoraDbContext db, IRouterClient router) : IDashboardQueries
{
    public async Task<DashboardDto> GetAsync(bool isManager, CancellationToken cancellationToken)
    {
        var customers = db.Customers.AsNoTracking().Where(c => isManager || !c.IsManagerOnly);
        var counts = await customers.GroupBy(c => c.Status)
            .Select(g => new { Status = g.Key, Total = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Total, cancellationToken);
        var pending = await (from request in db.ApprovalRequests
                             join customer in customers on request.CustomerId equals customer.Id
                             where request.Status == ApprovalStatus.Pending
                             select request.Id).CountAsync(cancellationToken);
        var recent = await (from activity in db.CustomerActivities.AsNoTracking()
                            join customer in customers on activity.CustomerId equals customer.Id
                            orderby activity.OccurredAtUtc descending, activity.Id descending
                            select new DashboardActivity(customer.Id, customer.FirstName + " " + customer.LastName,
                                activity.Action, activity.Outcome, activity.OccurredAtUtc)).Take(10).ToListAsync(cancellationToken);

        bool reachable;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(12));
        try
        {
            await router.GetIdentityAsync(budget.Token);
            reachable = true;
        }
        catch (RouterException)
        {
            reachable = false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            reachable = false;
        }

        return new DashboardDto(counts.Values.Sum(), counts.GetValueOrDefault(CustomerStatus.Active),
            counts.GetValueOrDefault(CustomerStatus.Disabled), counts.GetValueOrDefault(CustomerStatus.Terminated),
            counts.GetValueOrDefault(CustomerStatus.PendingApproval), pending,
            new DashboardRouter(router.Mode, reachable, DateTimeOffset.UtcNow), recent);
    }
}
