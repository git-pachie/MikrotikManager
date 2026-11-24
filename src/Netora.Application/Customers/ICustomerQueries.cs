namespace Netora.Application.Customers;

public interface ICustomerQueries
{
    /// <param name="includeManagerOnly">True only for Managers. Must come from the server-side role, never from the client.</param>
    Task<CustomerListDto> ListAsync(CustomerListQuery query, bool includeManagerOnly, CancellationToken cancellationToken);

    /// <summary>Provisional Plan and NAP options for the customer form (distinct values in use, NAP always includes UNKNOWN).</summary>
    Task<CustomerFormOptionsDto> GetFormOptionsAsync(bool includeManagerOnly, CancellationToken cancellationToken);

    /// <summary>
    /// One customer with its activity history and attachments. Returns null when the customer does not exist or is
    /// Manager-only and the viewer is not a Manager, so Staff cannot tell the two cases apart.
    /// </summary>
    /// <param name="viewerIsManager">From the server-side role. Controls visibility and the IsEditable flag.</param>
    Task<CustomerDetailDto?> GetDetailAsync(int id, bool viewerIsManager, CancellationToken cancellationToken);
}
