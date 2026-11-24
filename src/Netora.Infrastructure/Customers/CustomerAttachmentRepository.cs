using Microsoft.EntityFrameworkCore;
using Netora.Application.Customers.Attachments;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Customers;

internal sealed class CustomerAttachmentRepository(NetoraDbContext db) : ICustomerAttachmentRepository
{
    public Task<Customer?> FindVisibleCustomerAsync(int customerId, bool includeManagerOnly, CancellationToken cancellationToken)
        => db.Customers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == customerId && (includeManagerOnly || !c.IsManagerOnly), cancellationToken);

    public Task<CustomerAttachment?> FindAsync(int customerId, long attachmentId, CancellationToken cancellationToken)
        => db.CustomerAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.CustomerId == customerId && !a.IsDeleted, cancellationToken);

    public async Task AddAsync(CustomerAttachment attachment, CustomerActivity activity, CancellationToken cancellationToken)
    {
        db.CustomerAttachments.Add(attachment);
        db.CustomerActivities.Add(activity);
        await db.SaveChangesAsync(cancellationToken);   // one SaveChanges, so both rows or neither
    }

    public async Task SaveDeletedAsync(CustomerAttachment attachment, CustomerActivity activity, CancellationToken cancellationToken)
    {
        db.CustomerActivities.Add(activity);
        await db.SaveChangesAsync(cancellationToken);
    }
}
