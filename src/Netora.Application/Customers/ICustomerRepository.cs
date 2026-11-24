using Netora.Domain.Customers;

namespace Netora.Application.Customers;

public interface ICustomerRepository
{
    Task<bool> PppoeUsernameExistsAsync(string pppoeUsername, CancellationToken cancellationToken);

    /// <summary>Saves the customer. Returns false if the PPPoE user name was taken concurrently (unique index).</summary>
    Task<bool> TryAddAsync(Customer customer, CancellationToken cancellationToken);
}
