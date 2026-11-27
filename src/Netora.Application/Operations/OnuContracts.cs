using Netora.Application.Customers;

namespace Netora.Application.Operations;

public sealed record OnuDeviceDto(int CustomerId, string CustomerName, string Username, string? Serial, string? Model,
    string Nap, string Status);

public sealed record OnuReplacementDto(int CustomerId, string CustomerName, string? PreviousSerial, string? PreviousModel,
    string? NewSerial, string? NewModel, DateOnly ReplacedOn, string Reason, string Operator);

public sealed record OnuHistoryDto(IReadOnlyList<OnuDeviceDto> Devices, IReadOnlyList<OnuReplacementDto> Replacements,
    int Page, bool HasMoreDevices, bool HasMoreReplacements);

public sealed record ReplaceOnuRequest(string Serial, string DeviceModel, DateOnly ReplacedOn, string Reason, string ConcurrencyStamp);

public interface IOnuService
{
    Task<OnuHistoryDto?> ListAsync(int? customerId, string? search, int page, bool isManager, CancellationToken cancellationToken);
    Task<UpdateCustomerResult> ReplaceAsync(int customerId, ReplaceOnuRequest request, CustomerActor actor,
        CancellationToken cancellationToken);
}
