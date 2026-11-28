namespace Netora.Application.Operations;

public enum ReportType { CustomerStatus, SubscribersByNap, SubscribersByPlan, ProvisioningHistory }
public enum ReportRange { CurrentMonth, LastMonth, AllTime }
public sealed record ReportMetric(string Name, int Count);
public sealed record OperationsReportDto(ReportType Type, ReportRange Range, IReadOnlyList<ReportMetric> Metrics);
public sealed record NotificationItemDto(DateTimeOffset OccurredAtUtc, long RequestId, string Recipient, string Subject, string Status);
public sealed record BackupItemDto(DateTime FinishedAt, string Type, string? Destination);
public sealed record BackupHistoryDto(bool Available, IReadOnlyList<BackupItemDto> Items, string? Error = null);

public interface IOperationsPages
{
    Task<OperationsReportDto> ReportAsync(ReportType type, ReportRange range, bool isManager, CancellationToken ct);
    Task<IReadOnlyList<NotificationItemDto>> NotificationsAsync(string userId, bool isManager, CancellationToken ct);
    Task<BackupHistoryDto> BackupsAsync(CancellationToken ct);
}

public sealed record BackupSettingsDto(string? Destination, TimeOnly? DailyTimeSingapore, string? LastRunStatus = null,
    DateTimeOffset? LastRunAtUtc = null, bool ManualRunQueued = false);
public sealed record UpdateBackupSettingsRequest(string? Destination, TimeOnly? DailyTimeSingapore);

public interface IBackupManager
{
    Task<BackupSettingsDto> GetSettingsAsync(CancellationToken ct);
    Task<string?> SaveSettingsAsync(UpdateBackupSettingsRequest request, string actor, CancellationToken ct);
    Task<string?> QueueAsync(string actor, CancellationToken ct);
}
