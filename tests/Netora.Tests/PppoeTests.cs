using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Netora.Application.Pppoe;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Customers;
using Netora.Infrastructure.Persistence;
using Netora.Infrastructure.Pppoe;

namespace Netora.Tests;

public class PppoeTests
{
    private static PppoeCommands Commands(NetoraDbContext db) => new(
        new UpdatePppoeRequestValidator(),
        new CustomerRepository(db),
        new ApprovalRepository(db),
        new PppoePasswordProtector(new EphemeralDataProtectionProvider()),
        new RecordingNotifier(),
        NullLogger<PppoeCommands>.Instance);

    private static async Task<PppoeDetailDto> Get(TestDb t, int id, bool manager = true)
    {
        await using var db = t.NewContext();
        return (await new PppoeQueries(db).GetAsync(id, manager, default))!;
    }

    private static UpdatePppoeRequest Edit(PppoeDetailDto d, Func<UpdatePppoeRequest, UpdatePppoeRequest>? change = null)
    {
        var r = new UpdatePppoeRequest(d.PppoeUsername, d.PlanName, null, d.RemoteAddress, d.LocalAddress, d.Comment, d.ConcurrencyStamp);
        return change is null ? r : change(r);
    }

    private static async Task<CustomerStatus> StatusOf(TestDb t, int id)
    {
        await using var db = t.NewContext();
        return (await db.Customers.SingleAsync(c => c.Id == id)).Status;
    }

    // ----- editing -----

    [Fact]
    public async Task Active_accounts_cannot_be_edited_until_disabled()
    {
        var t = new TestDb(); var ids = t.Seed();
        var d = await Get(t, ids.Normal);
        Assert.False(d.CanEdit);
        Assert.True(d.CanRequestDisable);
        await using var db = t.NewContext();

        var result = await Commands(db).UpdateAsync(ids.Normal, Edit(d, r => r with { Comment = "x" }), TestDb.Manager, default);

        Assert.Equal(PppoeOutcome.AccountActive, result.Outcome);
    }

    [Fact]
    public async Task Disabled_accounts_can_be_edited_by_staff_including_username_plan_and_password()
    {
        var t = new TestDb(); var ids = t.Seed();
        await DisableAsManager(t, ids.Normal);
        var d = await Get(t, ids.Normal, manager: false);
        Assert.True(d.CanEdit);
        Assert.False(d.PasswordSet);
        await using var db = t.NewContext();

        var result = await Commands(db).UpdateAsync(ids.Normal, Edit(d, r => r with
        {
            PppoeUsername = "maria_new",
            PlanName = "Fiber 50 Mbps",
            NewPassword = "S3cret-Pass!",
            RemoteAddress = "10.0.0.25",
            Comment = "Moved to new building",
        }), TestDb.Staff, default);

        Assert.Equal(PppoeOutcome.Ok, result.Outcome);
        var after = await Get(t, ids.Normal);
        Assert.Equal("maria_new", after.PppoeUsername);
        Assert.Equal("Fiber 50 Mbps", after.PlanName);
        Assert.Equal("10.0.0.25", after.RemoteAddress);
        Assert.True(after.PasswordSet);
        Assert.Equal("NotSent", after.RouterSyncStatus);

        await using var check = t.NewContext();
        var stored = (await check.Customers.SingleAsync(c => c.Id == ids.Normal)).PppoePasswordProtected;
        Assert.NotNull(stored);
        Assert.DoesNotContain("S3cret-Pass!", stored);   // encrypted, not plain text

        var line = await check.CustomerActivities.OrderBy(a => a.Id).LastAsync(a => a.CustomerId == ids.Normal);
        Assert.Equal("PppoeUpdated", line.Action);
        Assert.Contains("Password", line.Detail);
        Assert.DoesNotContain("S3cret", line.Detail);    // value never in history
    }

    [Fact]
    public async Task Pending_approval_customers_can_be_edited()
    {
        var t = new TestDb(); var ids = t.Seed();
        var d = await Get(t, ids.Pending, manager: false);
        Assert.True(d.CanEdit);
        Assert.Equal("NotCreated", d.AccountState);
        await using var db = t.NewContext();

        var result = await Commands(db).UpdateAsync(ids.Pending, Edit(d, r => r with { NewPassword = "abcdefgh" }), TestDb.Staff, default);

        Assert.Equal(PppoeOutcome.Ok, result.Outcome);
    }

    [Fact]
    public async Task A_blank_password_keeps_the_stored_one()
    {
        var t = new TestDb(); var ids = t.Seed();
        var d = await Get(t, ids.Pending);
        await using (var db = t.NewContext())
            await Commands(db).UpdateAsync(ids.Pending, Edit(d, r => r with { NewPassword = "first-pass" }), TestDb.Staff, default);
        string? before;
        await using (var db = t.NewContext()) before = (await db.Customers.SingleAsync(c => c.Id == ids.Pending)).PppoePasswordProtected;

        var d2 = await Get(t, ids.Pending);
        await using (var db = t.NewContext())
            await Commands(db).UpdateAsync(ids.Pending, Edit(d2, r => r with { Comment = "only a comment" }), TestDb.Staff, default);

        await using var check = t.NewContext();
        Assert.Equal(before, (await check.Customers.SingleAsync(c => c.Id == ids.Pending)).PppoePasswordProtected);
    }

    [Theory]
    [InlineData("10.0.0.256")]
    [InlineData("10.0.0")]
    [InlineData("hello")]
    [InlineData("::1")]
    [InlineData("010.0.0.1")]
    public async Task Invalid_addresses_are_rejected(string address)
    {
        var t = new TestDb(); var ids = t.Seed();
        var d = await Get(t, ids.Pending);
        await using var db = t.NewContext();

        var result = await Commands(db).UpdateAsync(ids.Pending, Edit(d, r => r with { RemoteAddress = address }), TestDb.Staff, default);

        Assert.Equal(PppoeOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(nameof(UpdatePppoeRequest.RemoteAddress), result.Errors!.Keys);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("has space 1")]
    [InlineData("line\nbreak!")]
    public async Task Weak_or_unsafe_passwords_are_rejected(string password)
    {
        var t = new TestDb(); var ids = t.Seed();
        var d = await Get(t, ids.Pending);
        await using var db = t.NewContext();

        var result = await Commands(db).UpdateAsync(ids.Pending, Edit(d, r => r with { NewPassword = password }), TestDb.Staff, default);

        Assert.Equal(PppoeOutcome.ValidationFailed, result.Outcome);
    }

    [Fact]
    public async Task A_stale_form_is_rejected()
    {
        var t = new TestDb(); var ids = t.Seed();
        var opened = await Get(t, ids.Pending);
        await using (var db = t.NewContext())
            await Commands(db).UpdateAsync(ids.Pending, Edit(opened, r => r with { Comment = "first" }), TestDb.Staff, default);
        await using var db2 = t.NewContext();

        var stale = await Commands(db2).UpdateAsync(ids.Pending, Edit(opened, r => r with { Comment = "second" }), TestDb.Manager, default);

        Assert.Equal(PppoeOutcome.Conflict, stale.Outcome);
        Assert.Equal("first", (await Get(t, ids.Pending)).Comment);
    }

    [Fact]
    public async Task Terminated_staff_locked_manager_can_edit_but_not_username_or_plan()
    {
        var t = new TestDb(); var ids = t.Seed();
        var asStaff = await Get(t, ids.Terminated, manager: false);
        Assert.False(asStaff.CanEdit);
        await using var db = t.NewContext();

        var staff = await Commands(db).UpdateAsync(ids.Terminated, Edit(asStaff, r => r with { Comment = "x" }), TestDb.Staff, default);
        Assert.Equal(PppoeOutcome.CustomerLocked, staff.Outcome);

        var asManager = await Get(t, ids.Terminated);
        var rename = await Commands(db).UpdateAsync(ids.Terminated, Edit(asManager, r => r with { PppoeUsername = "renamed" }), TestDb.Manager, default);
        Assert.Equal(PppoeOutcome.ServiceFieldsLocked, rename.Outcome);

        var comment = await Commands(db).UpdateAsync(ids.Terminated, Edit(asManager, r => r with { Comment = "account closed" }), TestDb.Manager, default);
        Assert.Equal(PppoeOutcome.Ok, comment.Outcome);
    }

    // ----- disable / enable requests -----

    [Fact]
    public async Task Staff_disable_request_waits_for_a_manager()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using var db = t.NewContext();

        var result = await Commands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Disable", "Moving out"), TestDb.Staff, default);

        Assert.Equal(PppoeOutcome.Ok, result.Outcome);
        Assert.Equal("Pending", result.Request!.Status);
        Assert.Equal(CustomerStatus.Active, await StatusOf(t, ids.Normal));   // nothing changes before approval
        var d = await Get(t, ids.Normal);
        Assert.NotNull(d.PendingRequest);
        Assert.False(d.CanRequestDisable);
    }

    [Fact]
    public async Task Only_one_pending_request_per_account()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using var db = t.NewContext();
        await Commands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Disable", null), TestDb.Staff, default);

        var second = await Commands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Disable", null), TestDb.Staff, default);

        Assert.Equal(PppoeOutcome.RequestAlreadyPending, second.Outcome);
        Assert.Single(await db.ApprovalRequests.ToListAsync());
    }

    [Fact]
    public async Task Manager_approval_disables_the_account_in_the_database_only()
    {
        var t = new TestDb(); var ids = t.Seed();
        long requestId;
        await using (var db = t.NewContext())
            requestId = (await Commands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Disable", null), TestDb.Staff, default)).Request!.Id;
        var stampBefore = (await Get(t, ids.Normal)).ConcurrencyStamp;

        await using var db2 = t.NewContext();
        var result = await Commands(db2).DecideAsync(ids.Normal, requestId, new DecidePppoeRequest(true, "OK"), TestDb.Manager, default);

        Assert.Equal(PppoeOutcome.Ok, result.Outcome);
        var d = await Get(t, ids.Normal);
        Assert.Equal("Disabled", d.AccountState);
        Assert.Equal("NotSent", d.RouterSyncStatus);
        Assert.NotEqual(stampBefore, d.ConcurrencyStamp);   // open edit forms become stale
        Assert.True(d.CanEdit);
        Assert.True(d.CanRequestEnable);
        var request = Assert.Single(d.RecentRequests);
        Assert.Equal("Approved", request.Status);
        Assert.Equal("manager01", request.DecidedBy);
    }

    [Fact]
    public async Task Staff_cannot_decide_and_a_request_is_decided_only_once()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using var db = t.NewContext();
        var id = (await Commands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Disable", null), TestDb.Staff, default)).Request!.Id;

        Assert.Equal(PppoeOutcome.Forbidden, (await Commands(db).DecideAsync(ids.Normal, id, new(true, null), TestDb.Staff, default)).Outcome);
        Assert.Equal(PppoeOutcome.Ok, (await Commands(db).DecideAsync(ids.Normal, id, new(false, "No"), TestDb.Manager, default)).Outcome);
        Assert.Equal(PppoeOutcome.AlreadyDecided, (await Commands(db).DecideAsync(ids.Normal, id, new(true, null), TestDb.Manager, default)).Outcome);
        Assert.Equal(CustomerStatus.Active, await StatusOf(t, ids.Normal));   // rejected, then the late approval did nothing
    }

    [Fact]
    public async Task Two_managers_approving_at_once_only_one_wins()
    {
        var t = new TestDb(); var ids = t.Seed();
        long id;
        await using (var db = t.NewContext())
            id = (await Commands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Disable", null), TestDb.Staff, default)).Request!.Id;

        // Both load the pending request before either saves.
        await using var a = t.NewContext();
        await using var b = t.NewContext();
        var repoA = new ApprovalRepository(a);
        var repoB = new ApprovalRepository(b);
        var reqA = (await repoA.FindAsync(ids.Normal, id, default))!;
        var reqB = (await repoB.FindAsync(ids.Normal, id, default))!;
        reqA.Status = ApprovalStatus.Approved;
        reqB.Status = ApprovalStatus.Rejected;
        static CustomerActivity Line(int c) => new() { CustomerId = c, Action = "x", ActorUserId = "u", ActorUserName = "u", OccurredAtUtc = DateTimeOffset.UtcNow };

        Assert.True(await repoA.SaveDecisionAsync(reqA, null, null, Line(ids.Normal), default));
        Assert.False(await repoB.SaveDecisionAsync(reqB, null, null, Line(ids.Normal), default));
    }

    [Fact]
    public async Task Manager_requests_are_approved_at_once()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using var db = t.NewContext();

        var result = await Commands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Disable", null), TestDb.Manager, default);

        Assert.Equal("Approved", result.Request!.Status);
        Assert.Equal(CustomerStatus.Disabled, await StatusOf(t, ids.Normal));
        var actions = await db.CustomerActivities.Where(x => x.CustomerId == ids.Normal).Select(x => x.Action).ToListAsync();
        Assert.Contains("PppoeDisableRequested", actions);
        Assert.Contains("PppoeDisableApproved", actions);
    }

    [Fact]
    public async Task Enable_works_for_disabled_accounts_and_requests_must_fit_the_state()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using var db = t.NewContext();

        Assert.Equal(PppoeOutcome.NotAllowedInCurrentState,
            (await Commands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Enable", null), TestDb.Staff, default)).Outcome);
        Assert.Equal(PppoeOutcome.NotAllowedInCurrentState,
            (await Commands(db).SubmitAsync(ids.Pending, new SubmitPppoeRequest("Disable", null), TestDb.Staff, default)).Outcome);
        Assert.Equal(PppoeOutcome.ValidationFailed,
            (await Commands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Delete", null), TestDb.Staff, default)).Outcome);

        await DisableAsManager(t, ids.Normal);
        await using var db2 = t.NewContext();
        var enable = await Commands(db2).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Enable", null), TestDb.Manager, default);
        Assert.Equal(PppoeOutcome.Ok, enable.Outcome);
        Assert.Equal(CustomerStatus.Active, await StatusOf(t, ids.Normal));
    }

    [Fact]
    public async Task Approval_is_refused_if_the_account_changed_meanwhile()
    {
        var t = new TestDb(); var ids = t.Seed();
        long staffRequest;
        await using (var db = t.NewContext())
            staffRequest = (await Commands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Disable", null), TestDb.Staff, default)).Request!.Id;

        // Someone changes the status directly in the meantime (simulates another path, e.g. a future module).
        await using (var db = t.NewContext())
        {
            var c = await db.Customers.SingleAsync(x => x.Id == ids.Normal);
            c.Status = CustomerStatus.Disabled;
            await db.SaveChangesAsync();
        }

        await using var db2 = t.NewContext();
        var result = await Commands(db2).DecideAsync(ids.Normal, staffRequest, new(true, null), TestDb.Manager, default);
        Assert.Equal(PppoeOutcome.NotAllowedInCurrentState, result.Outcome);
    }

    [Fact]
    public async Task Staff_requests_on_terminated_customers_are_refused()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using var db = t.NewContext();
        Assert.Equal(PppoeOutcome.CustomerLocked,
            (await Commands(db).SubmitAsync(ids.Terminated, new SubmitPppoeRequest("Enable", null), TestDb.Staff, default)).Outcome);
    }

    // ----- visibility -----

    [Fact]
    public async Task Staff_never_see_accounts_of_manager_only_customers()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using var db = t.NewContext();
        var queries = new PppoeQueries(db);

        var staff = await queries.ListAsync(new PppoeListQuery(), false, default);
        var manager = await queries.ListAsync(new PppoeListQuery(), true, default);
        var search = await queries.ListAsync(new PppoeListQuery(Search: "harry"), false, default);

        Assert.DoesNotContain(staff.Items, i => i.PppoeUsername == "harry_hidden");
        Assert.Equal(3, staff.Stats.Total);
        Assert.Equal(1, staff.Stats.Enabled);
        Assert.Equal(4, manager.Stats.Total);
        Assert.Empty(search.Items);
        Assert.Null(await queries.GetAsync(ids.ManagerOnly, false, default));

        Assert.Equal(PppoeOutcome.NotFound, (await Commands(db).SubmitAsync(ids.ManagerOnly, new("Disable", null), TestDb.Staff, default)).Outcome);
        var md = (await queries.GetAsync(ids.ManagerOnly, true, default))!;
        Assert.Equal(PppoeOutcome.NotFound, (await Commands(db).UpdateAsync(ids.ManagerOnly, Edit(md), TestDb.Staff, default)).Outcome);
    }

    [Fact]
    public async Task List_filters_by_state_and_pending_request()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using (var db = t.NewContext())
            await Commands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("Disable", null), TestDb.Staff, default);
        await using var db2 = t.NewContext();
        var q = new PppoeQueries(db2);

        var requested = await q.ListAsync(new PppoeListQuery(State: "requested"), true, default);
        var notCreated = await q.ListAsync(new PppoeListQuery(State: "notcreated"), true, default);

        var row = Assert.Single(requested.Items);
        Assert.Equal("DisablePppoe", row.PendingOperation);
        Assert.Equal(1, requested.Stats.PendingRequests);
        Assert.Equal("pia_pending", Assert.Single(notCreated.Items).PppoeUsername);
    }

    [Fact]
    public void The_password_protector_round_trips_and_does_not_store_plain_text()
    {
        var protector = new PppoePasswordProtector(new EphemeralDataProtectionProvider());
        var stored = protector.Protect("router-Pass1");
        Assert.NotEqual("router-Pass1", stored);
        Assert.Equal("router-Pass1", protector.Unprotect(stored));
    }

    private static async Task DisableAsManager(TestDb t, int id)
    {
        await using var db = t.NewContext();
        var r = await Commands(db).SubmitAsync(id, new SubmitPppoeRequest("Disable", null), TestDb.Manager, default);
        Assert.Equal(PppoeOutcome.Ok, r.Outcome);
    }
}
