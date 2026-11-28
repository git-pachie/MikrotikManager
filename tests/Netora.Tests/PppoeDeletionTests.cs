using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Netora.Application.Approvals;
using Netora.Application.Audit;
using Netora.Application.Logging;
using Netora.Application.Pppoe;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Audit;
using Netora.Infrastructure.Customers;
using Netora.Infrastructure.Persistence;
using Netora.Infrastructure.Pppoe;
using Netora.Infrastructure.Router;

namespace Netora.Tests;

public sealed class PppoeDeletionTests
{
    private static readonly PppoePasswordProtector Passwords = new(new EphemeralDataProtectionProvider());

    [Fact]
    public async Task Managers_can_remove_the_router_secret_and_credentials_and_keep_customer_history()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        await using var db = fixture.NewContext();
        var customer = await db.Customers.SingleAsync(c => c.Id == ids.Normal);
        var router = new FakeRouter();
        var secret = router.Add(customer.PppoeUsername, "default");
        customer.RouterSecretId = secret.Id;
        customer.PppoePasswordProtected = Passwords.Protect("FixturePassword");
        await db.SaveChangesAsync();
        var stamp = new DeletePppoeRequest(customer.ConcurrencyStamp);
        var log = new ActivityRecorder();
        var provisioner = Provisioner(db, router, fixture, log);
        var commands = Commands(db, fixture, provisioner);
        var customers = fixture.Queries(db);
        var managerList = await customers.ListAsync(new(), true, default);
        Assert.True(managerList.Items.Single(c => c.Id == ids.Normal).CanDeletePppoe);
        Assert.True(managerList.Items.Single(c => c.Id == ids.Terminated).CanDeletePppoe);
        Assert.False((await customers.GetDetailAsync(ids.Normal, false, default))!.CanDeletePppoe);

        Assert.Equal(PppoeOutcome.Forbidden, (await commands.DeleteAsync(customer.Id, stamp, TestDb.Staff, default)).Outcome);
        Assert.Equal(PppoeOutcome.Conflict, (await commands.DeleteAsync(customer.Id, new("stale"), TestDb.Manager, default)).Outcome);
        Assert.Empty(fixture.Notifier.Deletions);
        Assert.Equal(0, router.Deletes);
        var result = await commands.DeleteAsync(customer.Id, stamp, TestDb.Manager, default);

        Assert.Equal(PppoeOutcome.Ok, result.Outcome);
        Assert.Equal("Succeeded", result.Request!.ProvisioningStatus);
        Assert.Empty(router.Secrets);
        Assert.Equal(1, router.Deletes);
        var account = await db.Customers.SingleAsync(c => c.Id == ids.Normal);
        Assert.Equal(CustomerStatus.Terminated, account.Status);
        Assert.NotNull(account.PppoeDeletedAtUtc);
        Assert.Null(account.RouterSecretId);
        Assert.Null(account.PppoePasswordProtected);
        Assert.Null(await new PppoeQueries(db).GetAsync(ids.Normal, true, default));
        Assert.DoesNotContain((await new PppoeQueries(db).ListAsync(new(), true, default)).Items, a => a.CustomerId == ids.Normal);
        var archived = (await customers.ListAsync(new(), true, default)).Items.Single(c => c.Id == ids.Normal);
        Assert.True(archived.PppoeDeleted);
        Assert.False(archived.CanDeletePppoe);
        Assert.False((await customers.GetDetailAsync(ids.Normal, true, default))!.CanDeletePppoe);
        var audit = await new AuditQueries(db).ListAsync(new AuditQuery(Category: AuditCategories.Pppoe), default);
        Assert.Contains(audit.Items, a => a.Action == "Deleted PPPoE account" && a.Actor == TestDb.Manager.UserName);
        Assert.Single(log.Events, a => a.Event == "AccountDeleted" && a.Category == ActivityCategories.Pppoe);
        Assert.Equal(result.Request.Id, Assert.Single(fixture.Notifier.Deletions).RequestId);
        await provisioner.ProvisionAsync(result.Request.Id, TestDb.Manager.UserId, TestDb.Manager.UserName, default);
        Assert.Equal(1, router.Deletes);
        Assert.Single(fixture.Notifier.Deletions);
    }

    [Fact]
    public async Task Lost_router_response_leaves_the_request_retryable_and_only_notifies_after_completion()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        await using var db = fixture.NewContext();
        var router = new FakeRouter { LoseDeleteResponse = true };
        router.Add("maria_santos", "default", comment: Netora.Application.Router.RouterRules.Marker(ids.Normal));
        var customer = await db.Customers.SingleAsync(c => c.Id == ids.Normal);
        var provisioner = Provisioner(db, router, fixture);
        var commands = Commands(db, fixture, provisioner);

        var first = await commands.DeleteAsync(ids.Normal, new(customer.ConcurrencyStamp), TestDb.Manager, default);
        Assert.Equal("Failed", first.Request!.ProvisioningStatus);
        Assert.Empty(fixture.Notifier.Deletions);
        Assert.Equal(CustomerStatus.Active, (await db.Customers.SingleAsync(c => c.Id == ids.Normal)).Status);
        Assert.NotNull(await new PppoeQueries(db).GetAsync(ids.Normal, true, default));
        Assert.Equal(PppoeOutcome.DeletionInProgress, (await commands.SubmitAsync(ids.Normal,
            new SubmitPppoeRequest("Disable", null), TestDb.Manager, default)).Outcome);

        var approvals = new ApprovalCommands(new ApprovalRepository(db), commands, provisioner);
        var retry = await approvals.RetryAsync(first.Request.Id, TestDb.Manager, default);
        Assert.Equal(RetryOutcome.Ok, retry.Outcome);
        Assert.Equal("Succeeded", retry.Result!.Status);
        Assert.Equal(1, router.Deletes);
        Assert.Single(fixture.Notifier.Deletions);
    }

    [Fact]
    public async Task Deleting_a_draft_closes_its_pending_creation_request_and_preserves_it_in_history()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        await using var db = fixture.NewContext();
        var router = new FakeRouter();
        var pending = new ApprovalRequest { CustomerId = ids.Pending, Operation = ApprovalOperation.CreatePppoe };
        db.ApprovalRequests.Add(pending);
        await db.SaveChangesAsync();
        var customer = await db.Customers.SingleAsync(c => c.Id == ids.Pending);

        var result = await Commands(db, fixture, Provisioner(db, router, fixture))
            .DeleteAsync(ids.Pending, new(customer.ConcurrencyStamp), TestDb.Manager, default);

        Assert.Equal("Succeeded", result.Request!.ProvisioningStatus);
        Assert.Equal(0, router.Deletes);
        var closed = await db.ApprovalRequests.SingleAsync(r => r.Id == pending.Id);
        Assert.Equal(ApprovalStatus.Rejected, closed.Status);
        Assert.Equal(TestDb.Manager.UserId, closed.DecidedByUserId);
        Assert.Single(fixture.Notifier.Deletions);
    }

    [Fact]
    public async Task Deletion_does_not_touch_an_unlinked_secret_with_the_same_username()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        await using var db = fixture.NewContext();
        var router = new FakeRouter();
        router.Add("maria_santos", "default");
        var customer = await db.Customers.SingleAsync(c => c.Id == ids.Normal);
        var command = Commands(db, fixture, Provisioner(db, router, fixture));

        var result = await command.DeleteAsync(ids.Normal, new(customer.ConcurrencyStamp), TestDb.Manager, default);

        Assert.Equal("Failed", result.Request!.ProvisioningStatus);
        Assert.Single(router.Secrets);
        Assert.Equal(0, router.Deletes);
        Assert.Empty(fixture.Notifier.Deletions);
        var request = await db.ApprovalRequests.SingleAsync(r => r.Id == result.Request.Id);
        Assert.Contains("could not be verified", request.ProvisioningError);
    }

    private static RouterProvisioner Provisioner(NetoraDbContext db, FakeRouter router, TestDb fixture, IActivityLog? log = null)
        => new(db, router, Passwords, NullLogger<RouterProvisioner>.Instance, log, fixture.Notifier);

    private static PppoeCommands Commands(NetoraDbContext db, TestDb fixture, RouterProvisioner provisioner)
        => new(new UpdatePppoeRequestValidator(), new CustomerRepository(db), new ApprovalRepository(db),
            Passwords, fixture.Notifier, provisioner, NullLogger<PppoeCommands>.Instance);

    private sealed class ActivityRecorder : IActivityLog
    {
        public List<ActivityEvent> Events { get; } = [];
        public void Write(ActivityEvent activity) => Events.Add(activity);
    }
}
