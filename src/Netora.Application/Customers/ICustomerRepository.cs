using Netora.Domain.Customers;

namespace Netora.Application.Customers;

public enum CustomerSaveResult
{
    Saved,

    /// <summary>The PPPoE user name was taken concurrently (unique index).</summary>
    DuplicatePppoeUsername,

    /// <summary>The customer was changed by someone else since it was read.</summary>
    Conflict,
}

public interface ICustomerRepository
{
    /// <summary>Case-insensitive check. <paramref name="exceptCustomerId"/> skips the customer being edited.</summary>
    Task<bool> PppoeUsernameExistsAsync(string pppoeUsername, int? exceptCustomerId, CancellationToken cancellationToken);

    /// <summary>
    /// Loads a customer for editing. Returns null if it does not exist or is Manager-only and
    /// <paramref name="includeManagerOnly"/> is false.
    /// </summary>
    Task<Customer?> FindForUpdateAsync(int id, bool includeManagerOnly, CancellationToken cancellationToken);

    /// <summary>Saves a new customer and its first activity line together, or neither.</summary>
    Task<CustomerSaveResult> AddAsync(Customer customer, CustomerActivity activity, CancellationToken cancellationToken);

    /// <summary>
    /// Saves an edited customer and its activity line together. <paramref name="expectedStamp"/> is the
    /// concurrency stamp the editor started from.
    /// </summary>
    Task<CustomerSaveResult> UpdateAsync(Customer customer, string expectedStamp, CustomerActivity activity,
        CancellationToken cancellationToken);
}
