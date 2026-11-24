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

    public Task<bool> PppoeUsernameExistsAsync(string pppoeUsername, CancellationToken cancellationToken)
        => db.Customers.AnyAsync(c => c.PppoeUsername == pppoeUsername, cancellationToken);

    public async Task<bool> TryAddAsync(Customer customer, CancellationToken cancellationToken)
    {
        db.Customers.Add(customer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sql && UniqueViolationErrors.Contains(sql.Number))
        {
            db.Entry(customer).State = EntityState.Detached;
            return false;
        }
    }
}
