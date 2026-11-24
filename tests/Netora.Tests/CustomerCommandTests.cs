using Microsoft.EntityFrameworkCore;
using Netora.Application.Customers;
using Netora.Domain.Customers;
using Netora.Infrastructure.Customers;

namespace Netora.Tests;

public class CustomerCreateTests
{
    [Fact]
    public async Task Create_saves_a_pending_customer_and_one_activity_line()
    {
        var t = new TestDb();
        await using var db = t.NewContext();

        var result = await t.Commands(db).CreateAsync(TestDb.NewCustomer(), TestDb.Staff, default);

        Assert.Equal(CreateCustomerOutcome.Created, result.Outcome);
        await using var check = t.NewContext();
        var customer = await check.Customers.SingleAsync();
        Assert.Equal(CustomerStatus.PendingApproval, customer.Status);
        Assert.Equal(TestDb.Staff.UserId, customer.CreatedByUserId);
        Assert.False(string.IsNullOrEmpty(customer.ConcurrencyStamp));

        var activity = await check.CustomerActivities.SingleAsync();
        Assert.Equal(customer.Id, activity.CustomerId);
        Assert.Equal("CustomerCreated", activity.Action);
        Assert.Equal("staff01", activity.ActorUserName);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_pppoe_username()
    {
        var t = new TestDb();
        await using var db = t.NewContext();
        await t.Commands(db).CreateAsync(TestDb.NewCustomer("dup_user"), TestDb.Manager, default);

        var second = await t.Commands(db).CreateAsync(TestDb.NewCustomer("dup_user"), TestDb.Staff, default);

        Assert.Equal(CreateCustomerOutcome.DuplicatePppoeUsername, second.Outcome);
        Assert.Single(await db.Customers.ToListAsync());
    }

    [Fact]
    public async Task Staff_cannot_create_a_manager_only_customer()
    {
        var t = new TestDb();
        await using var db = t.NewContext();

        var staff = await t.Commands(db).CreateAsync(TestDb.NewCustomer("a_user", managerOnly: true), TestDb.Staff, default);
        var manager = await t.Commands(db).CreateAsync(TestDb.NewCustomer("b_user", managerOnly: true), TestDb.Manager, default);

        Assert.Equal(CreateCustomerOutcome.ManagerOnlyNotAllowed, staff.Outcome);
        Assert.Equal(CreateCustomerOutcome.Created, manager.Outcome);
    }

    [Theory]
    [InlineData("ab")]               // too short
    [InlineData("has space")]
    [InlineData("semi;colon")]
    public async Task Create_rejects_invalid_pppoe_usernames(string username)
    {
        var t = new TestDb();
        await using var db = t.NewContext();

        var result = await t.Commands(db).CreateAsync(TestDb.NewCustomer(username), TestDb.Manager, default);

        Assert.Equal(CreateCustomerOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(nameof(CreateCustomerRequest.PppoeUsername), result.Errors!.Keys);
        Assert.Empty(await db.Customers.ToListAsync());
    }
}

public class CustomerUpdateTests
{
    private static async Task<CustomerDetailDto> Detail(TestDb t, int id, bool manager = true)
    {
        await using var db = t.NewContext();
        return (await t.Queries(db).GetDetailAsync(id, manager, default))!;
    }

    [Fact]
    public async Task Update_changes_the_fields_and_logs_field_names_but_not_values()
    {
        var t = new TestDb(); var ids = t.Seed();
        var before = await Detail(t, ids.Normal);
        await using var db = t.NewContext();

        var result = await t.Commands(db).UpdateAsync(ids.Normal,
            TestDb.EditOf(before, r => r with { MobileNumber = "0999 123 4567", Notes = "secret-note-text" }),
            TestDb.Staff, default);

        Assert.Equal(UpdateCustomerOutcome.Updated, result.Outcome);
        var after = await Detail(t, ids.Normal);
        Assert.Equal("0999 123 4567", after.MobileNumber);
        Assert.NotEqual(before.ConcurrencyStamp, after.ConcurrencyStamp);
        Assert.NotNull(after.UpdatedAtUtc);

        var line = Assert.Single(after.Activity);
        Assert.Equal("CustomerUpdated", line.Action);
        Assert.Equal("staff01", line.PerformedBy);
        Assert.Contains("Mobile Number", line.Detail);
        Assert.Contains("Notes", line.Detail);
        Assert.DoesNotContain("0999", line.Detail);          // no values in the history
        Assert.DoesNotContain("secret-note-text", line.Detail);
    }

    [Fact]
    public async Task Update_with_no_changes_writes_nothing()
    {
        var t = new TestDb(); var ids = t.Seed();
        var before = await Detail(t, ids.Normal);
        await using var db = t.NewContext();

        var result = await t.Commands(db).UpdateAsync(ids.Normal, TestDb.EditOf(before), TestDb.Manager, default);

        Assert.Equal(UpdateCustomerOutcome.NoChanges, result.Outcome);
        var after = await Detail(t, ids.Normal);
        Assert.Equal(before.ConcurrencyStamp, after.ConcurrencyStamp);
        Assert.Empty(after.Activity);
    }

    [Fact]
    public async Task A_stale_form_is_rejected_and_does_not_overwrite_the_newer_change()
    {
        var t = new TestDb(); var ids = t.Seed();
        var opened = await Detail(t, ids.Normal);   // two people open the same form

        await using (var first = t.NewContext())
        {
            var ok = await t.Commands(first).UpdateAsync(ids.Normal,
                TestDb.EditOf(opened, r => r with { Notes = "first editor" }), TestDb.Manager, default);
            Assert.Equal(UpdateCustomerOutcome.Updated, ok.Outcome);
        }

        await using var second = t.NewContext();
        var stale = await t.Commands(second).UpdateAsync(ids.Normal,
            TestDb.EditOf(opened, r => r with { Notes = "second editor" }), TestDb.Staff, default);

        Assert.Equal(UpdateCustomerOutcome.Conflict, stale.Outcome);
        Assert.Equal("first editor", (await Detail(t, ids.Normal)).Notes);
    }

    [Fact]
    public async Task The_repository_rejects_a_write_made_from_a_stale_stamp()
    {
        // Covers the race the command's early check cannot see: both editors passed it, then both save.
        var t = new TestDb(); var ids = t.Seed();
        await using var a = t.NewContext();
        await using var b = t.NewContext();
        var repoA = new CustomerRepository(a);
        var repoB = new CustomerRepository(b);

        var customerA = (await repoA.FindForUpdateAsync(ids.Normal, true, default))!;
        var customerB = (await repoB.FindForUpdateAsync(ids.Normal, true, default))!;
        var stamp = customerA.ConcurrencyStamp;

        customerB.Notes = "B wins";
        var savedB = await repoB.UpdateAsync(customerB, stamp, Line(ids.Normal), default);
        customerA.Notes = "A loses";
        var savedA = await repoA.UpdateAsync(customerA, stamp, Line(ids.Normal), default);

        Assert.Equal(CustomerSaveResult.Saved, savedB);
        Assert.Equal(CustomerSaveResult.Conflict, savedA);
        Assert.Equal("B wins", (await Detail(t, ids.Normal)).Notes);

        static CustomerActivity Line(int id) => new()
        {
            CustomerId = id, Action = "CustomerUpdated", ActorUserId = "u", ActorUserName = "u",
            OccurredAtUtc = DateTimeOffset.UtcNow,
        };
    }

    [Fact]
    public async Task Pending_customers_may_change_username_and_plan()
    {
        var t = new TestDb(); var ids = t.Seed();
        var before = await Detail(t, ids.Pending);
        Assert.False(before.ServiceFieldsLocked);
        await using var db = t.NewContext();

        var result = await t.Commands(db).UpdateAsync(ids.Pending,
            TestDb.EditOf(before, r => r with { PppoeUsername = "pia_new", PlanName = "Fiber 100 Mbps" }),
            TestDb.Staff, default);

        Assert.Equal(UpdateCustomerOutcome.Updated, result.Outcome);
        var after = await Detail(t, ids.Pending);
        Assert.Equal("pia_new", after.PppoeUsername);
        Assert.Equal("Fiber 100 Mbps", after.PlanName);
    }

    [Fact]
    public async Task Active_customers_cannot_change_username_or_plan_through_an_edit()
    {
        var t = new TestDb(); var ids = t.Seed();
        var before = await Detail(t, ids.Normal);
        Assert.True(before.ServiceFieldsLocked);
        await using var db = t.NewContext();

        var plan = await t.Commands(db).UpdateAsync(ids.Normal,
            TestDb.EditOf(before, r => r with { PlanName = "Fiber 30 Mbps" }), TestDb.Manager, default);
        var name = await t.Commands(db).UpdateAsync(ids.Normal,
            TestDb.EditOf(before, r => r with { PppoeUsername = "renamed" }), TestDb.Manager, default);

        Assert.Equal(UpdateCustomerOutcome.ServiceFieldsLocked, plan.Outcome);
        Assert.Contains(nameof(UpdateCustomerRequest.PlanName), plan.Errors!.Keys);
        Assert.Equal(UpdateCustomerOutcome.ServiceFieldsLocked, name.Outcome);
        var after = await Detail(t, ids.Normal);
        Assert.Equal("maria_santos", after.PppoeUsername);
        Assert.Equal("Fiber 100 Mbps", after.PlanName);
    }

    [Fact]
    public async Task Terminated_customers_are_view_only_for_staff()
    {
        var t = new TestDb(); var ids = t.Seed();
        var asStaff = await Detail(t, ids.Terminated, manager: false);
        Assert.False(asStaff.IsEditable);
        await using var db = t.NewContext();

        var result = await t.Commands(db).UpdateAsync(ids.Terminated,
            TestDb.EditOf(asStaff, r => r with { Notes = "x" }), TestDb.Staff, default);

        Assert.Equal(UpdateCustomerOutcome.Locked, result.Outcome);
        Assert.Null((await Detail(t, ids.Terminated)).Notes);
    }

    [Fact]
    public async Task Managers_can_still_edit_terminated_customers_details_but_not_username_or_plan()
    {
        var t = new TestDb(); var ids = t.Seed();
        var asManager = await Detail(t, ids.Terminated);
        Assert.True(asManager.IsEditable);
        Assert.True(asManager.ServiceFieldsLocked);
        await using var db = t.NewContext();

        var notes = await t.Commands(db).UpdateAsync(ids.Terminated,
            TestDb.EditOf(asManager, r => r with { Notes = "final bill sent" }), TestDb.Manager, default);
        Assert.Equal(UpdateCustomerOutcome.Updated, notes.Outcome);

        var after = await Detail(t, ids.Terminated);
        var rename = await t.Commands(db).UpdateAsync(ids.Terminated,
            TestDb.EditOf(after, r => r with { PppoeUsername = "tom_new" }), TestDb.Manager, default);
        Assert.Equal(UpdateCustomerOutcome.ServiceFieldsLocked, rename.Outcome);

        var final = await Detail(t, ids.Terminated);
        Assert.Equal("final bill sent", final.Notes);
        Assert.Equal("tom_term", final.PppoeUsername);
        Assert.Equal("Terminated", final.Status);
    }

    [Fact]
    public async Task Update_rejects_a_username_used_by_another_customer()
    {
        var t = new TestDb(); var ids = t.Seed();
        var pending = await Detail(t, ids.Pending);
        await using var db = t.NewContext();

        var result = await t.Commands(db).UpdateAsync(ids.Pending,
            TestDb.EditOf(pending, r => r with { PppoeUsername = "maria_santos" }), TestDb.Manager, default);

        Assert.Equal(UpdateCustomerOutcome.DuplicatePppoeUsername, result.Outcome);
        Assert.Equal("pia_pending", (await Detail(t, ids.Pending)).PppoeUsername);
    }

    [Fact]
    public async Task Only_a_manager_can_change_visibility()
    {
        var t = new TestDb(); var ids = t.Seed();
        var before = await Detail(t, ids.Normal);
        await using var db = t.NewContext();

        var staff = await t.Commands(db).UpdateAsync(ids.Normal,
            TestDb.EditOf(before, r => r with { IsManagerOnly = true }), TestDb.Staff, default);
        var manager = await t.Commands(db).UpdateAsync(ids.Normal,
            TestDb.EditOf(before, r => r with { IsManagerOnly = true }), TestDb.Manager, default);

        Assert.Equal(UpdateCustomerOutcome.ManagerOnlyNotAllowed, staff.Outcome);
        Assert.Equal(UpdateCustomerOutcome.Updated, manager.Outcome);
        // Once restricted, Staff can no longer see it.
        await using var check = t.NewContext();
        Assert.Null(await t.Queries(check).GetDetailAsync(ids.Normal, viewerIsManager: false, default));
    }

    [Fact]
    public async Task Update_validates_input()
    {
        var t = new TestDb(); var ids = t.Seed();
        var before = await Detail(t, ids.Normal);
        await using var db = t.NewContext();

        var result = await t.Commands(db).UpdateAsync(ids.Normal,
            TestDb.EditOf(before, r => r with { FirstName = "", Email = "not-an-email", MobileNumber = "abc" }),
            TestDb.Manager, default);

        Assert.Equal(UpdateCustomerOutcome.ValidationFailed, result.Outcome);
        Assert.Contains(nameof(UpdateCustomerRequest.FirstName), result.Errors!.Keys);
        Assert.Contains(nameof(UpdateCustomerRequest.Email), result.Errors.Keys);
        Assert.Contains(nameof(UpdateCustomerRequest.MobileNumber), result.Errors.Keys);
    }
}
