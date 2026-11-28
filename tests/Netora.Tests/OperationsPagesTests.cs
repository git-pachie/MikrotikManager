using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Netora.Application.Operations;
using Netora.Domain.Approvals;
using Netora.Infrastructure.Operations;

namespace Netora.Tests;

public sealed class OperationsPagesTests
{
    [Fact]
    public async Task Staff_reports_exclude_restricted_customers_and_their_provisioning_counts()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        await using var db = fixture.NewContext();
        var now = DateTimeOffset.UtcNow;

        foreach (var id in new[] { ids.Normal, ids.ManagerOnly })
            db.ApprovalRequests.Add(new ApprovalRequest
            {
                CustomerId = id, Status = ApprovalStatus.Approved, Operation = ApprovalOperation.ChangePlan,
                DecidedAtUtc = now, RequestedAtUtc = now, ProvisioningStatus = ProvisioningStatus.Succeeded,
            });
        await db.SaveChangesAsync();

        var pages = new OperationsPages(db, NullLogger<OperationsPages>.Instance);
        var customers = await pages.ReportAsync(ReportType.CustomerStatus, ReportRange.AllTime, false, default);
        Assert.Equal(3, customers.Metrics.Single(m => m.Name == "Total customers").Count);
        var plans = await pages.ReportAsync(ReportType.SubscribersByPlan, ReportRange.AllTime, false, default);
        Assert.DoesNotContain(plans.Metrics, m => m.Name == "Fiber 50 Mbps");
        var staff = await pages.ReportAsync(ReportType.ProvisioningHistory, ReportRange.CurrentMonth, false, default);
        var manager = await pages.ReportAsync(ReportType.ProvisioningHistory, ReportRange.CurrentMonth, true, default);
        Assert.Equal(1, staff.Metrics.Single(m => m.Name == "Succeeded").Count);
        Assert.Equal(2, manager.Metrics.Single(m => m.Name == "Succeeded").Count);
    }

    [Fact]
    public async Task Notifications_report_actual_outcomes_and_respect_requester_and_customer_visibility()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        await using var db = fixture.NewContext();
        var now = DateTimeOffset.UtcNow;
        var own = Request(ids.Normal, "staff", ApprovalStatus.Approved);
        var restricted = Request(ids.ManagerOnly, "staff", ApprovalStatus.Approved);
        var other = Request(ids.Terminated, "other", ApprovalStatus.Approved);
        var pending = Request(ids.Pending, "other", ApprovalStatus.Pending);
        db.ApprovalRequests.AddRange(own, restricted, other, pending);
        await db.SaveChangesAsync();
        db.ApprovalEmailDeliveries.AddRange(
            new ApprovalEmailDelivery { RequestId = own.Id, Kind = "Decided", Status = "Email failed", OccurredAtUtc = now },
            new ApprovalEmailDelivery { RequestId = own.Id, Kind = "PppoeCreationApproved", Status = "Accepted by SMTP", OccurredAtUtc = now });
        await db.SaveChangesAsync();

        var pages = new OperationsPages(db, NullLogger<OperationsPages>.Instance);
        var staff = await pages.NotificationsAsync("staff", false, default);
        Assert.All(staff, n => Assert.Equal(own.Id, n.RequestId));
        Assert.Contains(staff, n => n.Status == "Failed");
        Assert.Contains(staff, n => n.Status == "Email failed");
        Assert.DoesNotContain(staff, n => n.Recipient == "Admin");
        var manager = await pages.NotificationsAsync("manager", true, default);
        Assert.Contains(manager, n => n.RequestId == pending.Id && n.Status == "Awaiting approval");
        Assert.Contains(manager, n => n.Recipient == "Admin" && n.Status == "Accepted by SMTP");

        ApprovalRequest Request(int id, string requester, ApprovalStatus status) => new()
        {
            CustomerId = id, Status = status, Operation = ApprovalOperation.CreatePppoe,
            RequestedByUserId = requester, RequestedByUserName = requester, RequestedAtUtc = now,
            DecidedAtUtc = status == ApprovalStatus.Pending ? null : now,
            ProvisioningLastAttemptAtUtc = status == ApprovalStatus.Pending ? null : now,
            ProvisioningStatus = status == ApprovalStatus.Pending ? ProvisioningStatus.NotApplicable : ProvisioningStatus.Failed,
        };
    }

    [Theory]
    [InlineData(@"D:\NetoraBackups", true)]
    [InlineData("/mnt/netora-backups", true)]
    [InlineData(@"..\backup", false)]
    [InlineData(@"\\remote\share", false)]
    [InlineData("/mnt/../backups", false)]
    public void Backup_destination_must_be_an_absolute_SQL_host_directory(string path, bool expected)
        => Assert.Equal(expected, BackupManager.IsValidDestination(path));

    [Fact]
    public void Backup_filenames_are_unique_on_the_database_host()
    {
        var now = DateTimeOffset.UtcNow;
        var a = BackupManager.BackupPath(@"D:\NetoraBackups\", now);
        var b = BackupManager.BackupPath(@"D:\NetoraBackups\", now);
        Assert.StartsWith(@"D:\NetoraBackups\netora-", a);
        Assert.EndsWith(".bak", a);
        Assert.NotEqual(a, b);
    }
}
