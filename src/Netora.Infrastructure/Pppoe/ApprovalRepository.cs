using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Netora.Application.Pppoe;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Pppoe;

internal sealed class ApprovalRepository(NetoraDbContext db) : IApprovalRepository
{
    private static readonly int[] UniqueViolationErrors = [2601, 2627];

    public Task<ApprovalRequest?> FindPendingAsync(int customerId, CancellationToken cancellationToken)
        => db.ApprovalRequests.FirstOrDefaultAsync(r => r.CustomerId == customerId && r.Status == ApprovalStatus.Pending, cancellationToken);

    public Task<ApprovalRequest?> FindAsync(int customerId, long requestId, CancellationToken cancellationToken)
        => db.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == requestId && r.CustomerId == customerId, cancellationToken);

    public async Task<bool> AddAsync(ApprovalRequest request, Customer? changedCustomer, string? expectedCustomerStamp,
        IEnumerable<CustomerActivity> activity, CancellationToken cancellationToken)
    {
        db.ApprovalRequests.Add(request);
        db.CustomerActivities.AddRange(activity);
        ExpectStamp(changedCustomer, expectedCustomerStamp);
        return await TrySaveAsync(cancellationToken);
    }

    public async Task<bool> SaveDecisionAsync(ApprovalRequest request, Customer? changedCustomer, string? expectedCustomerStamp,
        CustomerActivity activity, CancellationToken cancellationToken)
    {
        // The UPDATE only matches while the request is still Pending, so two Managers deciding at once cannot both win.
        db.Entry(request).Property(r => r.Status).OriginalValue = ApprovalStatus.Pending;
        db.Entry(request).Property(r => r.Status).IsModified = true;
        db.CustomerActivities.Add(activity);
        ExpectStamp(changedCustomer, expectedCustomerStamp);
        return await TrySaveAsync(cancellationToken);
    }

    /// <summary>Makes the customer UPDATE conditional on the stamp read earlier, and rotates it.</summary>
    private void ExpectStamp(Customer? customer, string? expectedStamp)
    {
        if (customer is null || expectedStamp is null) return;
        db.Entry(customer).Property(c => c.ConcurrencyStamp).OriginalValue = expectedStamp;
        customer.ConcurrencyStamp = Guid.NewGuid().ToString("N");
    }

    private async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && UniqueViolationErrors.Contains(sql.Number))
        {
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
