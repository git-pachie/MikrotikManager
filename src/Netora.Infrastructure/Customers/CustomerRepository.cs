using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Netora.Application.Customers;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Customers;

internal sealed class CustomerRepository(NetoraDbContext db) : ICustomerRepository
{
    // SQL Server error numbers for unique index / constraint violations.
    private static readonly int[] UniqueViolationErrors = [2601, 2627];

    public Task<bool> PppoeUsernameExistsAsync(string pppoeUsername, int? exceptCustomerId, CancellationToken cancellationToken)
        => db.Customers.AnyAsync(
            c => c.PppoeUsername == pppoeUsername && (exceptCustomerId == null || c.Id != exceptCustomerId),
            cancellationToken);

    public Task<Customer?> FindForUpdateAsync(int id, bool includeManagerOnly, CancellationToken cancellationToken)
        => db.Customers.Include(c => c.Plan).Include(c => c.Nap)
            .FirstOrDefaultAsync(c => c.Id == id && (includeManagerOnly || !c.IsManagerOnly), cancellationToken);

    public Task<Domain.Plans.InternetPlan?> FindPlanAsync(string name, CancellationToken cancellationToken)
        => db.InternetPlans.FirstOrDefaultAsync(p => p.Name == name, cancellationToken);

    public Task<Domain.Plans.InternetPlan?> FindPlanByIdAsync(int id, CancellationToken cancellationToken)
        => db.InternetPlans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Domain.Network.NapBox?> FindNapAsync(string code, CancellationToken cancellationToken)
        => db.NapBoxes.FirstOrDefaultAsync(n => n.Code == code, cancellationToken);

    public async Task<CustomerSaveResult> AddAsync(Customer customer, CustomerActivity activity,
        Domain.Approvals.ApprovalRequest creationRequest, CancellationToken cancellationToken)
    {
        // The activity and the request need the new customer's ID, so all rows are written in one transaction.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Customers.Add(customer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            activity.CustomerId = customer.Id;
            creationRequest.CustomerId = customer.Id;
            db.CustomerActivities.Add(activity);
            db.ApprovalRequests.Add(creationRequest);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CustomerSaveResult.Saved;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return CustomerSaveResult.DuplicatePppoeUsername;
        }
    }

    public async Task<CustomerSaveResult> UpdateAsync(Customer customer, string expectedStamp, CustomerActivity activity,
        CancellationToken cancellationToken)
    {
        // The UPDATE only matches if nobody saved in between: WHERE Id = @id AND ConcurrencyStamp = @expected.
        db.Entry(customer).Property(c => c.ConcurrencyStamp).OriginalValue = expectedStamp;
        customer.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        db.CustomerActivities.Add(activity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return CustomerSaveResult.Saved;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return CustomerSaveResult.Conflict;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
            return CustomerSaveResult.DuplicatePppoeUsername;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is SqlException sql && UniqueViolationErrors.Contains(sql.Number);
}
