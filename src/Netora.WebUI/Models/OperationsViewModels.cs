using System.ComponentModel.DataAnnotations;
using Netora.Application.Customers;
using Netora.Application.Operations;
using Netora.Application.Router;

namespace Netora.WebUI.Models;

public sealed record DiagnosticViewModel(CustomerListDto? Customers, CustomerDetailDto? Selected, PppoeDiagnosticsDto? Result,
    string? Error, string? Search, string Check, int Page);

public sealed record OnuHistoryViewModel(OnuHistoryDto? History, int? CustomerId, string? Search, string? Error);

public sealed class ReplaceOnuViewModel
{
    public CustomerDetailDto? Customer { get; set; }

    [Required, StringLength(50)]
    public string Serial { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string DeviceModel { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    public DateOnly ReplacedOn { get; set; } = DateOnly.FromDateTime(DateTime.Now);

    [Required, StringLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public string ConcurrencyStamp { get; set; } = string.Empty;
}
