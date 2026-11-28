using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Netora.Application.Operations;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Operations;

internal sealed class OperationsPages(NetoraDbContext db, ILogger<OperationsPages> logger) : IOperationsPages
{
    public async Task<OperationsReportDto> ReportAsync(ReportType type, ReportRange range, bool isManager, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8));
        var month = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        var from = range == ReportRange.LastMonth ? month.AddMonths(-1) : month;
        var to = range == ReportRange.LastMonth ? month : month.AddMonths(1);
        var visible = db.Customers.AsNoTracking().Where(c => isManager || !c.IsManagerOnly);
        List<ReportMetric> metrics;

        if (type == ReportType.ProvisioningHistory)
        {
            var requests = from r in db.ApprovalRequests.AsNoTracking()
                           join c in visible on r.CustomerId equals c.Id
                           where r.Status == ApprovalStatus.Approved
                           select r;
            if (range != ReportRange.AllTime)
                requests = requests.Where(r => r.DecidedAtUtc >= from && r.DecidedAtUtc < to);
            var counts = await requests.GroupBy(r => r.ProvisioningStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Status, g => g.Count, ct);
            metrics =
            [
                new("Succeeded", counts.GetValueOrDefault(ProvisioningStatus.Succeeded)),
                new("Failed", counts.GetValueOrDefault(ProvisioningStatus.Failed)),
                new("Not sent", counts.GetValueOrDefault(ProvisioningStatus.NotSent)),
                new("Superseded", counts.GetValueOrDefault(ProvisioningStatus.Superseded)),
            ];
        }
        else
        {
            if (range != ReportRange.AllTime)
                visible = visible.Where(c => c.CreatedAtUtc >= from && c.CreatedAtUtc < to);
            if (type is ReportType.SubscribersByNap or ReportType.SubscribersByPlan)
            {
                var names = type == ReportType.SubscribersByNap
                    ? visible.Select(c => c.Nap.Code) : visible.Select(c => c.Plan.Name);
                metrics = await names.GroupBy(name => name).OrderBy(g => g.Key)
                    .Select(g => new ReportMetric(g.Key, g.Count())).ToListAsync(ct);
            }
            else
            {
                var counts = await visible.GroupBy(c => c.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Status, g => g.Count, ct);
                metrics =
                [
                    new("Active customers", counts.GetValueOrDefault(CustomerStatus.Active)),
                    new("Disabled customers", counts.GetValueOrDefault(CustomerStatus.Disabled)),
                    new("Pending approval", counts.GetValueOrDefault(CustomerStatus.PendingApproval)),
                    new("Terminated customers", counts.GetValueOrDefault(CustomerStatus.Terminated)),
                ];
            }
        }

        metrics.Add(new(type == ReportType.ProvisioningHistory ? "Total approved requests" : "Total customers", metrics.Sum(m => m.Count)));
        return new(type, range, metrics);
    }

    public async Task<IReadOnlyList<NotificationItemDto>> NotificationsAsync(string userId, bool isManager, CancellationToken ct)
    {
        const int limit = 100;
        var visible = from r in db.ApprovalRequests.AsNoTracking()
                      join c in db.Customers.AsNoTracking() on r.CustomerId equals c.Id
                      where isManager || (!c.IsManagerOnly && r.RequestedByUserId == userId)
                      select r;
        var items = new List<NotificationItemDto>();

        if (isManager)
        {
            var pending = await visible.Where(r => r.Status == ApprovalStatus.Pending)
                .OrderByDescending(r => r.RequestedAtUtc).ThenByDescending(r => r.Id).Take(limit).ToListAsync(ct);
            items.AddRange(pending.Select(r => new NotificationItemDto(r.RequestedAtUtc, r.Id, "Managers",
                Approvals.ApprovalEmailDispatcher.OperationLabel(r.Operation), "Awaiting approval")));
        }

        var decisions = await visible.Where(r => r.DecidedAtUtc != null)
            .OrderByDescending(r => r.DecidedAtUtc).ThenByDescending(r => r.Id).Take(limit).ToListAsync(ct);
        items.AddRange(decisions.Select(r => new NotificationItemDto(r.DecidedAtUtc!.Value, r.Id, r.RequestedByUserName,
            Approvals.ApprovalEmailDispatcher.OperationLabel(r.Operation), r.Status.ToString())));

        var outcomes = await visible.Where(r => r.ProvisioningLastAttemptAtUtc != null)
            .OrderByDescending(r => r.ProvisioningLastAttemptAtUtc).ThenByDescending(r => r.Id).Take(limit).ToListAsync(ct);
        items.AddRange(outcomes.Select(r => new NotificationItemDto(r.ProvisioningLastAttemptAtUtc!.Value, r.Id, r.RequestedByUserName,
            $"Router update: {Approvals.ApprovalEmailDispatcher.OperationLabel(r.Operation)}", r.ProvisioningStatus.ToString())));

        var emails = await (from delivery in db.ApprovalEmailDeliveries.AsNoTracking()
                            join r in visible on delivery.RequestId equals r.Id
                            where isManager || delivery.Kind == nameof(Approvals.ApprovalNoticeKind.Decided)
                            orderby delivery.OccurredAtUtc descending, delivery.Id descending
                            select new { delivery, r.RequestedByUserName })
            .Take(limit).ToListAsync(ct);
        items.AddRange(emails.Select(e => new NotificationItemDto(e.delivery.OccurredAtUtc, e.delivery.RequestId,
            e.delivery.Kind switch
            {
                nameof(Approvals.ApprovalNoticeKind.Decided) => e.RequestedByUserName,
                nameof(Approvals.ApprovalNoticeKind.PppoeCreationApproved) => "Admin",
                _ => "Managers",
            },
            e.delivery.Kind switch
            {
                nameof(Approvals.ApprovalNoticeKind.Decided) => "Approval decision email",
                nameof(Approvals.ApprovalNoticeKind.PppoeCreationApproved) => "New PPPoE account email",
                nameof(Approvals.ApprovalNoticeKind.PppoeDeleted) => "PPPoE deletion email",
                _ => "Approval requested email",
            }, e.delivery.Status)));

        return items.OrderByDescending(i => i.OccurredAtUtc).ThenByDescending(i => i.RequestId).Take(limit).ToList();
    }

    public async Task<BackupHistoryDto> BackupsAsync(CancellationToken ct)
    {
        try
        {
            await db.Database.OpenConnectionAsync(ct);
            try
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandType = CommandType.Text;
                command.CommandTimeout = 10;
                command.CommandText = """
                    SELECT TOP (50) b.backup_finish_date, b.type, b.is_copy_only,
                           (SELECT STRING_AGG(CONVERT(nvarchar(max), m.physical_device_name), N'; ')
                            FROM msdb.dbo.backupmediafamily m WHERE m.media_set_id = b.media_set_id)
                    FROM msdb.dbo.backupset b
                    WHERE b.database_name = DB_NAME() AND b.backup_finish_date IS NOT NULL
                    ORDER BY b.backup_finish_date DESC, b.backup_set_id DESC
                    """;
                await using var reader = await command.ExecuteReaderAsync(ct);
                var rows = new List<BackupItemDto>();
                while (await reader.ReadAsync(ct))
                {
                    var type = reader.GetString(1) switch
                    {
                        "D" => reader.GetBoolean(2) ? "Full copy-only" : "Full database",
                        "I" => "Differential",
                        "L" => "Transaction log",
                        "F" or "G" => "File or filegroup",
                        "P" or "Q" => "Partial database",
                        _ => "Other backup",
                    };
                    rows.Add(new(reader.GetDateTime(0), type, reader.IsDBNull(3) ? null : reader.GetString(3)));
                }
                return new(true, rows);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
        catch (SqlException ex)
        {
            logger.LogWarning("BackupHistoryUnavailable SqlErrorNumber={Number}", ex.Number);
            return new(false, [], "SQL Server backup history is unavailable to Netora. Check access to the backup history in msdb.");
        }
    }
}
