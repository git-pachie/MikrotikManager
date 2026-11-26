using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Netora.Application.Catalog;
using Netora.Application.Customers;
using Netora.Application.Pppoe;
using Netora.Application.Router;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Catalog;
using Netora.Infrastructure.Persistence;
using Netora.Infrastructure.Pppoe;
using Netora.Infrastructure.Router;

namespace Netora.Tests;

/// <summary>Modules 05 Internet Plans and 06 NAP Boxes, with the rules agreed on 2026-11-26.</summary>
public class CatalogTests
{
    private static PlanService Plans(NetoraDbContext db, FakeRouter? router = null)
        => new(db, new SavePlanRequestValidator(), router ?? new FakeRouter(), NullLogger<PlanService>.Instance);

    private static NapService Naps(NetoraDbContext db) => new(db, new SaveNapRequestValidator(), NullLogger<NapService>.Instance);

    private static SavePlanRequest NewPlan(string name = "Plan 799", string profile = "NETORA_60M", bool active = true, string? stamp = null)
        => new(name, 799m, 60, 60, profile, active, stamp);

    private static async Task<Customer> CustomerOf(TestDb t, int id)
    {
        await using var db = t.NewContext();
        return await db.Customers.Include(c => c.Plan).Include(c => c.Nap).SingleAsync(c => c.Id == id);
    }

    private static async Task Deactivate(TestDb t, string? plan = null, string? nap = null)
    {
        await using var db = t.NewContext();
        if (plan is not null) (await db.InternetPlans.SingleAsync(p => p.Name == plan)).IsActive = false;
        if (nap is not null) (await db.NapBoxes.SingleAsync(n => n.Code == nap)).IsActive = false;
        await db.SaveChangesAsync();
    }

    // ----- 05.01 plan catalogue -----

    [Fact]
    public async Task Managers_add_plans_and_staff_cannot()
    {
        var t = new TestDb();
        await using var db = t.NewContext();

        var staff = await Plans(db).CreateAsync(NewPlan(), TestDb.Staff, default);
        var created = await Plans(db).CreateAsync(NewPlan(), TestDb.Manager, default);
        var duplicate = await Plans(db).CreateAsync(NewPlan(name: "plan 799"), TestDb.Manager, default);
        var invalid = await Plans(db).CreateAsync(new SavePlanRequest("Bad", -1, 0, null, "", true), TestDb.Manager, default);

        Assert.Equal(CatalogOutcome.Forbidden, staff.Outcome);
        Assert.Equal(CatalogOutcome.Ok, created.Outcome);
        Assert.Equal(CatalogOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(CatalogOutcome.ValidationFailed, invalid.Outcome);
        Assert.Contains(nameof(SavePlanRequest.MonthlyFee), invalid.Errors!.Keys);
        Assert.Contains(nameof(SavePlanRequest.RouterProfile), invalid.Errors!.Keys);
        Assert.Contains(nameof(SavePlanRequest.DownloadMbps), invalid.Errors!.Keys);

        var plan = (await Plans(db).GetAsync(created.Id!.Value, true, default))!;
        Assert.Equal(("Plan 799", 799m, "NETORA_60M", true), (plan.Name, plan.MonthlyFee!.Value, plan.RouterProfile, plan.IsActive));
    }

    [Fact]
    public async Task Editing_a_plan_needs_the_current_stamp_and_keeps_customers_linked()
    {
        var t = new TestDb(); var ids = t.Seed();
        var planId = t.PlanIds["Fiber 100 Mbps"];
        string stamp;
        await using (var db = t.NewContext()) stamp = (await Plans(db).GetAsync(planId, true, default))!.ConcurrencyStamp;

        await using (var db = t.NewContext())
        {
            var renamed = await Plans(db).UpdateAsync(planId, NewPlan("Plan 899", "NETORA_100M", stamp: stamp), TestDb.Manager, default);
            Assert.Equal(CatalogOutcome.Ok, renamed.Outcome);
        }
        await using (var db = t.NewContext())
        {
            var stale = await Plans(db).UpdateAsync(planId, NewPlan("Plan 999", stamp: stamp), TestDb.Manager, default);
            Assert.Equal(CatalogOutcome.Conflict, stale.Outcome);
        }

        var customer = await CustomerOf(t, ids.Normal);
        Assert.Equal(("Plan 899", "NETORA_100M"), (customer.Plan.Name, customer.Plan.RouterProfile));
        Assert.Equal(RouterSyncStatus.NotSent, customer.RouterSyncStatus);   // the router is not touched
    }

    [Fact]
    public async Task Subscriber_counts_leave_out_manager_only_customers_for_staff()
    {
        var t = new TestDb(); t.Seed();
        await using var db = t.NewContext();

        var staff = await Plans(db).ListAsync(false, default);
        var manager = await Plans(db).ListAsync(true, default);

        int Count(PlanListDto list, string name) => list.Items.Single(p => p.Name == name).Subscribers;
        Assert.Equal(0, Count(staff, "Fiber 50 Mbps"));     // only the Manager-only customer uses it
        Assert.Equal(1, Count(manager, "Fiber 50 Mbps"));
        Assert.Equal(2, Count(staff, "Fiber 30 Mbps"));     // pending and terminated count too
    }

    [Fact]
    public async Task Inactive_plans_cannot_be_chosen_but_customers_keep_them()
    {
        var t = new TestDb(); var ids = t.Seed();
        await Deactivate(t, plan: "Fiber 100 Mbps");

        await using (var db = t.NewContext())
        {
            var create = await t.Commands(db).CreateAsync(TestDb.NewCustomer() with { PlanName = "Fiber 100 Mbps" }, TestDb.Manager, default);
            Assert.Equal(CreateCustomerOutcome.ValidationFailed, create.Outcome);
            Assert.Contains(nameof(CreateCustomerRequest.PlanName), create.Errors!.Keys);
        }

        // Maria (Active, on the now inactive plan) can still be edited; her plan stays.
        await using (var db = t.NewContext())
        {
            var detail = (await t.Queries(db).GetDetailAsync(ids.Normal, true, default))!;
            var edit = await t.Commands(db).UpdateAsync(ids.Normal, TestDb.EditOf(detail, r => r with { Notes = "Gate is blue" }), TestDb.Manager, default);
            Assert.Equal(UpdateCustomerOutcome.Updated, edit.Outcome);
        }
        Assert.Equal("Fiber 100 Mbps", (await CustomerOf(t, ids.Normal)).Plan.Name);

        // A plan change to an inactive plan is refused (05.02).
        await Deactivate(t, plan: "Fiber 300 Mbps");
        await using (var db = t.NewContext())
        {
            var r = await t.PppoeCommands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("ChangePlan", null, "Fiber 300 Mbps"), TestDb.Staff, default);
            Assert.Equal(PppoeOutcome.ValidationFailed, r.Outcome);
        }
    }

    [Fact]
    public async Task An_approved_plan_change_applies_the_requested_plan_even_after_a_rename()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using (var db = t.NewContext())
            await t.PppoeCommands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("ChangePlan", null, "Fiber 300 Mbps"), TestDb.Staff, default);

        await using (var db = t.NewContext())
        {
            var plan = await db.InternetPlans.SingleAsync(p => p.Name == "Fiber 300 Mbps");
            plan.Name = "Fiber 300 Promo";
            await db.SaveChangesAsync();
        }

        long requestId;
        await using (var db = t.NewContext())
        {
            var request = await db.ApprovalRequests.SingleAsync(r => r.CustomerId == ids.Normal && r.Status == ApprovalStatus.Pending);
            Assert.Equal(t.PlanIds["Fiber 300 Mbps"], request.TargetPlanId);
            Assert.Equal("Fiber 300 Mbps", request.TargetPlanName);   // name at request time, kept for history
            requestId = request.Id;
        }
        await using (var db = t.NewContext())
        {
            var decided = await t.PppoeCommands(db).DecideAsync(ids.Normal, requestId, new DecidePppoeRequest(true, null), TestDb.Manager, default);
            Assert.Equal(PppoeOutcome.Ok, decided.Outcome);
        }

        Assert.Equal("Fiber 300 Promo", (await CustomerOf(t, ids.Normal)).Plan.Name);
    }

    [Fact]
    public async Task Provisioning_sends_the_plans_mapped_profile_not_its_name()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using (var db = t.NewContext())
        {
            (await db.InternetPlans.SingleAsync(p => p.Name == "Fiber 100 Mbps")).RouterProfile = "NETORA_100M";
            db.ApprovalRequests.Add(new ApprovalRequest
            {
                CustomerId = ids.Normal, Operation = ApprovalOperation.DisablePppoe, Status = ApprovalStatus.Approved,
                ProvisioningStatus = ProvisioningStatus.NotSent, RequestedByUserId = "m", RequestedByUserName = "manager01",
                RequestedAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var router = new FakeRouter();
        router.Add("maria_santos", "Fiber 100 Mbps");

        await using (var db = t.NewContext())
        {
            var id = (await db.ApprovalRequests.SingleAsync()).Id;
            var result = await new RouterProvisioner(db, router, new PppoePasswordProtector(new EphemeralDataProtectionProvider()),
                NullLogger<RouterProvisioner>.Instance).ProvisionAsync(id, "m", "manager01", default);
            Assert.Equal("Succeeded", result.Status);
        }
        Assert.Equal("NETORA_100M", router.Secrets.Single().Profile);

        // The status check now expects the mapped profile.
        await using (var db = t.NewContext())
        {
            var status = (await new RouterStatusService(db, router).CheckAsync(ids.Normal, true, default))!;
            Assert.Equal("NETORA_100M", status.ExpectedProfile);
            Assert.DoesNotContain(status.Mismatches, m => m.Field == "Profile (Internet Plan)");
        }
    }

    [Fact]
    public async Task Router_check_reports_found_and_missing_profiles_and_survives_a_dead_router()
    {
        var t = new TestDb();
        var router = new FakeRouter();
        router.Profiles.AddRange(["Fiber 30 Mbps", "Fiber 50 Mbps", "VIP"]);
        await using var db = t.NewContext();

        var check = await Plans(db, router).CheckRouterAsync(default);
        router.Down = true;
        var down = await Plans(db, router).CheckRouterAsync(default);

        Assert.True(check.Reachable);
        Assert.True(check.Plans.Single(p => p.Name == "Fiber 50 Mbps").Found);
        Assert.False(check.Plans.Single(p => p.Name == "Fiber 100 Mbps").Found);
        Assert.Equal(["default", "VIP"], check.UnusedRouterProfiles);
        Assert.False(down.Reachable);
        Assert.Contains("could not be reached", down.Error);
        Assert.Equal(0, router.Creates + router.Updates);
    }

    [Fact]
    public async Task Import_needs_a_plan_for_the_secrets_profile()
    {
        var t = new TestDb(); t.Seed();
        var router = new FakeRouter();
        router.Add("new_one", "NOT_A_PLAN");
        await using var db = t.NewContext();

        var scan = await new PppoeImportService(db, router, new PppoePasswordProtector(new EphemeralDataProtectionProvider()),
            NullLogger<PppoeImportService>.Instance).ScanAsync(default);

        var item = Assert.Single(scan.Items);
        Assert.Equal("Cannot import", item.State);
        Assert.Contains("NOT_A_PLAN", item.Reason);
    }

    // ----- 06 NAP boxes -----

    [Fact]
    public async Task Managers_add_nap_boxes_unknown_is_reserved_and_read_only()
    {
        var t = new TestDb();
        await using var db = t.NewContext();

        var staff = await Naps(db).CreateAsync(new SaveNapRequest("NAP-09", "Sumacab", null, true), TestDb.Staff, default);
        var created = await Naps(db).CreateAsync(new SaveNapRequest("NAP-09", "Sumacab", "Barangay hall", true), TestDb.Manager, default);
        var duplicate = await Naps(db).CreateAsync(new SaveNapRequest("nap-09", null, null, true), TestDb.Manager, default);
        var reserved = await Naps(db).CreateAsync(new SaveNapRequest("unknown", null, null, true), TestDb.Manager, default);
        var unknown = await Naps(db).UpdateAsync(t.NapIds[Customer.UnknownNap], new SaveNapRequest("UNKNOWN2", null, null, false, "x"), TestDb.Manager, default);

        Assert.Equal(CatalogOutcome.Forbidden, staff.Outcome);
        Assert.Equal(CatalogOutcome.Ok, created.Outcome);
        Assert.Equal(CatalogOutcome.Duplicate, duplicate.Outcome);
        Assert.Equal(CatalogOutcome.ValidationFailed, reserved.Outcome);
        Assert.Equal(CatalogOutcome.Locked, unknown.Outcome);
    }

    [Fact]
    public async Task Nap_lists_and_details_hide_manager_only_customers_from_staff()
    {
        var t = new TestDb(); var ids = t.Seed();
        await Deactivate(t, nap: "NAP-03");
        await using var db = t.NewContext();

        var staffList = await Naps(db).ListAsync(false, default);
        var managerList = await Naps(db).ListAsync(true, default);
        var staffNap02 = (await Naps(db).GetAsync(t.NapIds["NAP-02"], false, default))!;
        var managerNap02 = (await Naps(db).GetAsync(t.NapIds["NAP-02"], true, default))!;

        Assert.Equal(0, staffList.Items.Single(n => n.Code == "NAP-02").Customers);
        Assert.Equal(1, managerList.Items.Single(n => n.Code == "NAP-02").Customers);
        Assert.Equal(Customer.UnknownNap, staffList.Items.Last().Code);   // built-in NAP listed last
        Assert.Empty(staffNap02.AssignedCustomers);
        Assert.Equal(ids.ManagerOnly, Assert.Single(managerNap02.AssignedCustomers).CustomerId);
        Assert.Equal(["NAP-01", Customer.UnknownNap], managerNap02.MoveTargets);   // not itself, not the inactive NAP-03
    }

    [Fact]
    public async Task Moving_a_customer_to_another_nap_is_recorded_and_follows_the_rules()
    {
        var t = new TestDb(); var ids = t.Seed();
        await Deactivate(t, nap: "NAP-03");

        async Task<UpdateCustomerResult> Move(int id, string nap, CustomerActor actor, string? stamp = null)
        {
            await using var db = t.NewContext();
            stamp ??= (await db.Customers.SingleAsync(c => c.Id == id)).ConcurrencyStamp;
            return await t.Commands(db).MoveNapAsync(id, new MoveNapRequest(nap, stamp), actor, default);
        }

        Assert.Equal(UpdateCustomerOutcome.Updated, (await Move(ids.Pending, "NAP-02", TestDb.Staff)).Outcome);
        Assert.Equal(UpdateCustomerOutcome.ValidationFailed, (await Move(ids.Pending, "NAP-03", TestDb.Staff)).Outcome);
        Assert.Equal(UpdateCustomerOutcome.Conflict, (await Move(ids.Pending, "NAP-01", TestDb.Staff, stamp: "stale")).Outcome);
        Assert.Equal(UpdateCustomerOutcome.Locked, (await Move(ids.Terminated, "NAP-02", TestDb.Staff)).Outcome);
        Assert.Equal(UpdateCustomerOutcome.NotFound, (await Move(ids.ManagerOnly, "NAP-01", TestDb.Staff)).Outcome);
        Assert.Equal(UpdateCustomerOutcome.Updated, (await Move(ids.Terminated, "NAP-02", TestDb.Manager)).Outcome);

        Assert.Equal("NAP-02", (await CustomerOf(t, ids.Pending)).Nap.Code);
        await using var check = t.NewContext();
        var activity = await check.CustomerActivities.Where(a => a.CustomerId == ids.Pending).SingleAsync();
        Assert.Equal(("CustomerUpdated", "Changed: NAP Box", "staff01"), (activity.Action, activity.Detail, activity.ActorUserName));
    }

    [Fact]
    public async Task Customers_cannot_be_put_on_an_inactive_nap_but_keep_one_they_have()
    {
        var t = new TestDb(); var ids = t.Seed();
        await Deactivate(t, nap: "NAP-01");
        await using var db = t.NewContext();

        var create = await t.Commands(db).CreateAsync(TestDb.NewCustomer(), TestDb.Manager, default);   // NAP-01
        Assert.Equal(CreateCustomerOutcome.ValidationFailed, create.Outcome);
        Assert.Contains(nameof(CreateCustomerRequest.NapBox), create.Errors!.Keys);

        var unknown = await t.Commands(db).CreateAsync(TestDb.NewCustomer() with { NapBox = null }, TestDb.Manager, default);
        Assert.Equal(CreateCustomerOutcome.Created, unknown.Outcome);
        Assert.Equal(Customer.UnknownNap, (await CustomerOf(t, unknown.CustomerId!.Value)).Nap.Code);

        var detail = (await t.Queries(db).GetDetailAsync(ids.Normal, true, default))!;   // Maria is on NAP-01
        var edit = await t.Commands(db).UpdateAsync(ids.Normal, TestDb.EditOf(detail, r => r with { Notes = "Kept NAP" }), TestDb.Manager, default);
        Assert.Equal(UpdateCustomerOutcome.Updated, edit.Outcome);
    }
}
