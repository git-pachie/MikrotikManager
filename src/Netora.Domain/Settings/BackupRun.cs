namespace Netora.Domain.Settings;

public sealed class BackupRun
{
    public long Id { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public string ActorUserId { get; set; } = string.Empty;
    public bool Scheduled { get; set; }
    public string Status { get; set; } = string.Empty;
}
