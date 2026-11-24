using Netora.Domain.Customers;

namespace Netora.Application.Customers.Attachments;

/// <summary>File storage for attachment content. The Web API is the only process that reads or writes it.</summary>
public interface IAttachmentStore
{
    /// <summary>
    /// Writes <paramref name="header"/> followed by the rest of <paramref name="content"/> to
    /// <paramref name="storedFileName"/>. Stops and removes the partial file once more than
    /// <paramref name="maxBytes"/> have been written. Returns the size written, or null if the limit was exceeded.
    /// </summary>
    Task<long?> SaveAsync(string storedFileName, ReadOnlyMemory<byte> header, Stream content, long maxBytes,
        CancellationToken cancellationToken);

    /// <summary>Opens a stored file for reading, or returns null if it is missing from the storage folder.</summary>
    Stream? OpenRead(string storedFileName);

    /// <summary>Removes a file. Used only to clean up after a failed save, never for a user's delete.</summary>
    void DeleteQuietly(string storedFileName);
}

public interface ICustomerAttachmentRepository
{
    /// <summary>The customer, if it exists and the caller may see it (Manager-only rule).</summary>
    Task<Customer?> FindVisibleCustomerAsync(int customerId, bool includeManagerOnly, CancellationToken cancellationToken);

    /// <summary>A non-deleted attachment of that customer, or null.</summary>
    Task<CustomerAttachment?> FindAsync(int customerId, long attachmentId, CancellationToken cancellationToken);

    /// <summary>Saves the attachment row and its activity line together.</summary>
    Task AddAsync(CustomerAttachment attachment, CustomerActivity activity, CancellationToken cancellationToken);

    /// <summary>Saves a soft-deleted attachment and its activity line together.</summary>
    Task SaveDeletedAsync(CustomerAttachment attachment, CustomerActivity activity, CancellationToken cancellationToken);
}
