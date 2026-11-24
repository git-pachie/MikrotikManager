namespace Netora.Application.Customers;

public interface ICustomerQueries
{
    /// <param name="includeManagerOnly">True only for Managers. Must come from the server-side role, never from the client.</param>
    Task<CustomerListDto> ListAsync(CustomerListQuery query, bool includeManagerOnly, CancellationToken cancellationToken);

    /// <summary>Provisional Plan and NAP options for the customer form (distinct values in use, NAP always includes UNKNOWN).</summary>
    Task<CustomerFormOptionsDto> GetFormOptionsAsync(bool includeManagerOnly, CancellationToken cancellationToken);
}
