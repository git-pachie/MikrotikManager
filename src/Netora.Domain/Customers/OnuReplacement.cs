namespace Netora.Domain.Customers;

/// <summary>A snapshot of both devices and the operator at the time the change was recorded.</summary>
public sealed class OnuReplacement
{
    public long Id { get; set; }
    public int CustomerId { get; init; }
    public string? PreviousSerial { get; init; }
    public string? PreviousModel { get; init; }
    public string? NewSerial { get; init; }
    public string? NewModel { get; init; }
    public DateOnly ReplacedOn { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string OperatorId { get; init; } = string.Empty;
    public string Operator { get; init; } = string.Empty;
}
