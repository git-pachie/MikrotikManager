namespace Netora.Domain.Approvals;

public sealed class ApprovalEmailDelivery
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
