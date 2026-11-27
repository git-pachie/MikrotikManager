using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Netora.Application.Customers;
using Netora.Application.Operations;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Identity;
using Netora.Infrastructure.Operations;
using Netora.Infrastructure.Router;

namespace Netora.Tests;

public class OperationsTests
{
    [Fact]
    public async Task Dashboard_counts_and_activity_respect_visibility_even_when_the_router_is_down()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        await using var db = fixture.NewContext();
        db.ApprovalRequests.AddRange(
            new ApprovalRequest { CustomerId = ids.Normal, Status = ApprovalStatus.Pending },
            new ApprovalRequest { CustomerId = ids.ManagerOnly, Status = ApprovalStatus.Pending });
        db.CustomerActivities.AddRange(
            new CustomerActivity { CustomerId = ids.Normal, Action = "CustomerCreated" },
            new CustomerActivity { CustomerId = ids.ManagerOnly, Action = "CustomerCreated" });
        await db.SaveChangesAsync();
        var dashboard = new DashboardQueries(db, new FakeRouter { Down = true });

        var staff = await dashboard.GetAsync(false, default);
        var manager = await dashboard.GetAsync(true, default);

        Assert.Equal((3, 1, 1, 1), (staff.Total, staff.Active, staff.Terminated, staff.PendingRequests));
        Assert.Equal(ids.Normal, Assert.Single(staff.RecentActivity).CustomerId);
        Assert.False(staff.Router.Reachable);
        Assert.Equal((4, 2, 2), (manager.Total, manager.Active, manager.PendingRequests));
        Assert.Equal(2, manager.RecentActivity.Count);
    }

    [Fact]
    public async Task Replacement_preserves_both_devices_and_operator_and_blocks_hidden_or_stale_writes()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        await using var db = fixture.NewContext();
        var customer = await db.Customers.SingleAsync(c => c.Id == ids.Normal);
        customer.OnuSerial = "old-serial";
        customer.OnuModel = "Old model";
        await db.SaveChangesAsync();
        var date = DateOnly.FromDateTime(DateTime.Now);
        var request = new ReplaceOnuRequest("new-serial", "New model", date, "Device failure", customer.ConcurrencyStamp);
        var service = new OnuService(db);

        Assert.Equal(UpdateCustomerOutcome.NotFound, (await service.ReplaceAsync(ids.ManagerOnly, request, TestDb.Staff, default)).Outcome);
        Assert.Equal(UpdateCustomerOutcome.Locked, (await service.ReplaceAsync(ids.Terminated, request, TestDb.Staff, default)).Outcome);
        Assert.Equal(UpdateCustomerOutcome.ValidationFailed,
            (await service.ReplaceAsync(ids.Normal, request with { ReplacedOn = date.AddDays(1) }, TestDb.Staff, default)).Outcome);
        Assert.Equal(UpdateCustomerOutcome.Updated, (await service.ReplaceAsync(ids.Normal, request, TestDb.Staff, default)).Outcome);
        Assert.Equal(UpdateCustomerOutcome.Conflict, (await service.ReplaceAsync(ids.Normal, request, TestDb.Staff, default)).Outcome);
        Assert.Null(await service.ListAsync(ids.ManagerOnly, null, 1, false, default));

        customer.Status = CustomerStatus.Terminated;
        await db.SaveChangesAsync();
        var history = await service.ListAsync(ids.Normal, "old-serial", 1, false, default);
        Assert.Equal("new-serial", Assert.Single(history!.Devices).Serial);
        var replacement = Assert.Single(history.Replacements);
        Assert.Equal(("old-serial", "Old model", "new-serial", "New model", date, "Device failure", "staff01"),
            (replacement.PreviousSerial, replacement.PreviousModel, replacement.NewSerial, replacement.NewModel,
                replacement.ReplacedOn, replacement.Reason, replacement.Operator));
    }

    [Fact]
    public async Task A_token_revocation_is_persisted_and_does_not_revoke_other_sessions()
    {
        using var fixture = new TestDb();
        var id = Guid.NewGuid();
        await using (var db = fixture.NewContext())
        {
            var tokens = new AccessTokenRevocations(db, NullLogger<AccessTokenRevocations>.Instance);
            await tokens.RevokeAsync(id, DateTimeOffset.UtcNow.AddHours(1), default);
        }

        await using var next = fixture.NewContext();
        var check = new AccessTokenRevocations(next, NullLogger<AccessTokenRevocations>.Instance);
        Assert.True(await check.ContainsAsync(id, default));
        Assert.False(await check.ContainsAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public void Router_logs_return_only_recognized_events_for_the_exact_customer()
    {
        Assert.Equal("Connected", RouterOsRestClient.ParsePppoeLog("sam", "pppoe,info", "<pppoe-sam>: connected", "08:30:00")?.Event);
        Assert.Equal("Authentication failed", RouterOsRestClient.ParsePppoeLog("sam", "ppp,error", "user sam authentication failed", null)?.Event);
        Assert.Null(RouterOsRestClient.ParsePppoeLog("sam", "pppoe,info", "<pppoe-sam_other>: connected", null));
        Assert.Null(RouterOsRestClient.ParsePppoeLog("sam", "pppoe,debug", "<pppoe-sam>: unstructured message", null));
        Assert.Null(RouterOsRestClient.ParsePppoeLog("sam", "system,info", "<pppoe-sam>: connected", null));
    }
}
