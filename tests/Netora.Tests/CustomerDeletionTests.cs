using Microsoft.EntityFrameworkCore;
using Netora.Application.Audit;
using Netora.Application.Customers;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Approvals;
using Netora.Infrastructure.Audit;
using Netora.Infrastructure.Operations;

namespace Netora.Tests;

public sealed class CustomerDeletionTests
{
    [Fact]
    public async Task Customer_deletion_requires_a_manager_and_completed_local_pppoe_deletion()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        await using var db = fixture.NewContext();
        var customer = await db.Customers.SingleAsync(c => c.Id == ids.Normal);
        var commands = fixture.Commands(db, new FakeRouter());
        var request = new DeleteCustomerRequest(customer.ConcurrencyStamp);

        Assert.Equal(DeleteCustomerOutcome.Forbidden, (await Delete(commands, customer.Id, request, TestDb.Staff)));
        Assert.Equal(DeleteCustomerOutcome.PppoeDeletionRequired, await Delete(commands, customer.Id, request, TestDb.Manager));
        customer.PppoeDeletionRequestedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Equal(DeleteCustomerOutcome.PppoeDeletionRequired, await Delete(commands, customer.Id, request, TestDb.Manager));
        Assert.Null(customer.DeletedAtUtc);
        Assert.Empty(await db.CustomerActivities.ToListAsync());
    }

    [Theory]
    [InlineData("MARIA_SANTOS", false)]
    [InlineData("renamed_on_router", true)]
    public async Task A_restored_or_renamed_router_secret_blocks_customer_deletion(string username, bool hasMarker)
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        await using var db = fixture.NewContext();
        var customer = await PrepareAsync(db, ids.Normal);
        var router = new FakeRouter();
        router.Add(username, "default", comment: hasMarker ? Netora.Application.Router.RouterRules.Marker(ids.Normal) : null);

        var result = await Delete(fixture.Commands(db, router), customer.Id, new(customer.ConcurrencyStamp), TestDb.Manager);

        Assert.Equal(DeleteCustomerOutcome.PppoeExistsOnRouter, result);
        Assert.Single(router.Secrets);
        Assert.Null(customer.DeletedAtUtc);
    }

    [Fact]
    public async Task Customer_deletion_fails_closed_when_the_router_cannot_be_read_or_the_form_is_stale()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        await using var db = fixture.NewContext();
        var customer = await PrepareAsync(db, ids.Normal);
        var commands = fixture.Commands(db, new FakeRouter { Down = true });

        Assert.Equal(DeleteCustomerOutcome.RouterUnavailable,
            await Delete(commands, customer.Id, new(customer.ConcurrencyStamp), TestDb.Manager));
        Assert.Equal(DeleteCustomerOutcome.Conflict, await Delete(commands, customer.Id, new("stale"), TestDb.Manager));
        Assert.Null(customer.DeletedAtUtc);
    }

    [Fact]
    public async Task Deleting_a_customer_removes_it_from_operational_reads_and_preserves_manager_history()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        await using var db = fixture.NewContext();
        var customer = await PrepareAsync(db, ids.Normal);
        var request = new ApprovalRequest { CustomerId = customer.Id, Operation = ApprovalOperation.DeletePppoe,
            Status = ApprovalStatus.Approved, ProvisioningStatus = ProvisioningStatus.Succeeded };
        db.ApprovalRequests.Add(request);
        db.CustomerActivities.Add(new CustomerActivity { CustomerId = customer.Id, Action = "CustomerCreated" });
        await db.SaveChangesAsync();
        var router = new FakeRouter();
        var commands = fixture.Commands(db, router);

        Assert.Equal(DeleteCustomerOutcome.Deleted,
            await Delete(commands, customer.Id, new(customer.ConcurrencyStamp), TestDb.Manager));

        var customers = fixture.Queries(db);
        Assert.Null(await customers.GetDetailAsync(customer.Id, true, default));
        Assert.DoesNotContain((await customers.ListAsync(new(), true, default)).Items, c => c.Id == customer.Id);
        Assert.Equal(3, (await new DashboardQueries(db, router).GetAsync(true, default)).Total);
        Assert.Null(await new OnuService(db).ListAsync(customer.Id, null, 1, true, default));
        Assert.NotNull((await db.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == customer.Id)).DeletedAtUtc);
        var audit = await new AuditQueries(db).ListAsync(new AuditQuery(Category: AuditCategories.Customers), default);
        Assert.Contains(audit.Items, i => i.Action == "Deleted customer" && i.Actor == TestDb.Manager.UserName && i.EntityLink is null);
        Assert.Contains(audit.Items, i => i.Action == "Added customer" && i.EntityLink is null);
        var approvals = new ApprovalQueries(db);
        Assert.True((await approvals.GetAsync(request.Id, true, default))!.CustomerDeleted);
        Assert.Null(await approvals.GetAsync(request.Id, false, default));
    }

    private static Task<DeleteCustomerOutcome> Delete(ICustomerCommands commands, int id, DeleteCustomerRequest request, CustomerActor actor)
        => commands.DeleteAsync(id, request, actor, default);

    private static async Task<Customer> PrepareAsync(Netora.Infrastructure.Persistence.NetoraDbContext db, int id)
    {
        var customer = await db.Customers.SingleAsync(c => c.Id == id);
        customer.Status = CustomerStatus.Terminated;
        customer.PppoeDeletionRequestedAtUtc = DateTimeOffset.UtcNow;
        customer.PppoeDeletedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        return customer;
    }
}
