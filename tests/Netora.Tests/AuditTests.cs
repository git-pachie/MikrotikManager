using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Netora.Application.Audit;
using Netora.Application.Catalog;
using Netora.Application.Pppoe;
using Netora.Domain.Security;
using Netora.Infrastructure.Audit;
using Netora.Infrastructure.Catalog;
using Netora.Infrastructure.Identity;

namespace Netora.Tests;

/// <summary>Module 12.01 Audit Trail.</summary>
public class AuditTests
{
    [Theory]
    [InlineData("PppoeChangePlanRequested", "Requested plan change")]
    [InlineData("PppoeCreateApproved", "Approved PPPoE creation")]
    [InlineData("PppoeTerminateRejected", "Rejected termination")]
    [InlineData("CustomerUpdated", "Edited customer")]
    [InlineData("SomethingNew", "SomethingNew")]
    public void Actions_read_like_the_mockup(string action, string label) => Assert.Equal(label, AuditLabels.Action(action));

    [Theory]
    [InlineData("Role Staff, by admin", "admin", "Role Staff")]
    [InlineData("by admin", "admin", null)]
    [InlineData("Router settings, password changed", null, "Router settings, password changed")]
    [InlineData(null, null, null)]
    public void Administration_reasons_split_into_actor_and_detail(string? reason, string? actor, string? detail)
        => Assert.Equal((actor, detail), AuditLabels.SplitBy(reason));

    /// <summary>Events from every source, at known times (oldest first).</summary>
    private static async Task<TestDb> SeedAsync()
    {
        var t = new TestDb();
        var ids = t.Seed();
        var day = new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
        await using (var db = t.NewContext())
        {
            db.Users.Add(new ApplicationUser { Id = "manager-id", UserName = "manager01", NormalizedUserName = "MANAGER01" });
            db.SecurityEvents.AddRange(
                new SecurityEvent { TimestampUtc = day, EventType = SecurityEventTypes.LoginFailed, Succeeded = false, UserName = "staff01", IpAddress = "10.0.0.9", Reason = "WrongPassword" },
                new SecurityEvent { TimestampUtc = day.AddMinutes(1), EventType = SecurityEventTypes.LoginSucceeded, Succeeded = true, UserId = "staff-id", UserName = "staff01" },
                new SecurityEvent { TimestampUtc = day.AddMinutes(2), EventType = SecurityEventTypes.UserCreated, Succeeded = true, UserId = "new-id", UserName = "staff02", Reason = "Role Staff, by manager01" },
                new SecurityEvent { TimestampUtc = day.AddDays(2), EventType = SecurityEventTypes.SettingsUpdated, Succeeded = true, UserId = "manager-id", Reason = "Email settings, sending on" });
            await db.SaveChangesAsync();
        }
        await using (var db = t.NewContext())
            await t.PppoeCommands(db).SubmitAsync(ids.Normal, new SubmitPppoeRequest("ChangePlan", "Faster please", "Fiber 300 Mbps"), TestDb.Staff, default);
        await using (var db = t.NewContext())
            await new PlanService(db, new SavePlanRequestValidator(), new FakeRouter(), NullLogger<PlanService>.Instance)
                .CreateAsync(new SavePlanRequest("Plan 799", 799, 60, 60, "NETORA_60M", true), TestDb.Manager, default);
        return t;
    }

    private static async Task<AuditListDto> List(TestDb t, AuditQuery q)
    {
        await using var db = t.NewContext();
        return await new AuditQueries(db).ListAsync(q, default);
    }

    [Fact]
    public async Task The_trail_combines_every_source_newest_first()
    {
        var t = await SeedAsync();
        var all = await List(t, new AuditQuery());

        Assert.Equal(6, all.TotalMatching);   // 4 security events, 1 approval request, 1 plan
        Assert.Equal(all.Items.OrderByDescending(i => i.OccurredAtUtc).ToList(), all.Items);
        Assert.Contains(all.Items, i => i is { Category: "Approvals", Action: "Requested plan change", Actor: "staff01", Entity: "Maria Santos", Result: "Pending" });
        Assert.Contains(all.Items, i => i is { Category: "Plans & NAP", Action: "Added plan", Actor: "manager01", Entity: "Plan Plan 799" });
        var created = Assert.Single(all.Items, i => i.Action == "Added user");
        Assert.Equal(("manager01", "User staff02", "Role Staff"), (created.Actor, created.Entity, created.Detail));
        var settings = Assert.Single(all.Items, i => i.Action == "Changed settings");
        Assert.Equal(("manager01", "/settings"), (settings.Actor, settings.EntityLink));
        var failed = Assert.Single(all.Items, i => i.Action == "Sign-in failed");
        Assert.Equal(("staff01", "Failed", "WrongPassword, IP 10.0.0.9"), (failed.Actor, failed.Result, failed.Detail));
    }

    [Fact]
    public async Task Filters_by_category_actor_search_and_date()
    {
        var t = await SeedAsync();

        var signIn = await List(t, new AuditQuery(Category: AuditCategories.SignIn));
        var byManager = await List(t, new AuditQuery(Actor: "manager01"));
        var maria = await List(t, new AuditQuery(Search: "Maria"));
        var day = await List(t, new AuditQuery(From: new DateOnly(2026, 11, 20), To: new DateOnly(2026, 11, 20)));

        Assert.Equal(["Signed in", "Sign-in failed"], signIn.Items.Select(i => i.Action));
        Assert.Equal(["Added plan", "Changed settings", "Added user"], byManager.Items.Select(i => i.Action));   // newest first
        Assert.Equal("Requested plan change", Assert.Single(maria.Items).Action);
        Assert.Equal(3, day.TotalMatching);   // the three sign-in and user events of Nov 20
    }

    [Fact]
    public async Task Pages_are_cut_from_the_merged_list()
    {
        var t = await SeedAsync();
        var all = (await List(t, new AuditQuery(PageSize: 100))).Items;

        // Page size 25 is the smallest allowed; ask for unsupported 2 and get 25.
        var odd = await List(t, new AuditQuery(PageSize: 2));
        var beyond = await List(t, new AuditQuery(Page: 2));

        Assert.Equal(25, odd.PageSize);
        Assert.Equal(all, odd.Items);
        Assert.Empty(beyond.Items);
        Assert.Equal(6, beyond.TotalMatching);
    }

    [Fact]
    public async Task Plan_and_nap_changes_are_recorded_with_the_change()
    {
        var t = new TestDb();
        var planId = t.PlanIds["Fiber 50 Mbps"];
        await using (var db = t.NewContext())
        {
            var plans = new PlanService(db, new SavePlanRequestValidator(), new FakeRouter(), NullLogger<PlanService>.Instance);
            var stamp = (await plans.GetAsync(planId, true, default))!.ConcurrencyStamp;
            await plans.UpdateAsync(planId, new SavePlanRequest("Fiber 50 Mbps", 799, null, null, "Fiber 50 Mbps", false, stamp), TestDb.Manager, default);
            await new NapService(db, new SaveNapRequestValidator(), NullLogger<NapService>.Instance)
                .CreateAsync(new SaveNapRequest("NAP-09", "Sumacab", null, true), TestDb.Manager, default);
        }

        await using var check = t.NewContext();
        var rows = await check.AuditEvents.OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(["PlanUpdated", "PlanDeactivated", "NapCreated"], rows.Select(r => r.Action));
        Assert.Equal("Changed: Monthly Fee, Status", rows[0].Detail);
        Assert.All(rows, r => Assert.Equal("manager01", r.ActorUserName));
    }
}
