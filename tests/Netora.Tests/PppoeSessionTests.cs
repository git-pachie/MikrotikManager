using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Netora.Application.Approvals;
using Netora.Application.Auth;
using Netora.Application.Pppoe;
using Netora.Application.Router;
using Netora.Domain.Customers;
using Netora.Infrastructure.Approvals;
using Netora.Infrastructure.Pppoe;
using Netora.Infrastructure.Router;

namespace Netora.Tests;

public sealed class PppoeSessionTests
{
    [Fact]
    public async Task Traffic_sampling_checks_visibility_and_distinguishes_offline_from_router_errors()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        await using var db = fixture.NewContext();
        var router = new FakeRouter();
        var service = new RouterStatusService(db, router);

        Assert.Equal("mock", (await service.SampleTrafficAsync(ids.Normal, false, default))!.State);
        router.Mode = "RouterOS";
        Assert.Null(await service.SampleTrafficAsync(ids.ManagerOnly, false, default));
        Assert.Equal("no_account", (await service.SampleTrafficAsync(ids.Pending, false, default))!.State);
        Assert.Empty(router.TrafficReads);

        Assert.Equal("offline", (await service.SampleTrafficAsync(ids.Normal, false, default))!.State);
        var expected = new PppoeTrafficCounters("connection", 125, 50);
        router.Traffic.Add("maria_santos", expected);
        var sample = (await service.SampleTrafficAsync(ids.Normal, false, default))!;
        Assert.Equal("online", sample.State);
        Assert.Equal(expected, sample.Counters);
        Assert.Equal(["maria_santos", "maria_santos"], router.TrafficReads);

        router.Down = true;
        sample = (await service.SampleTrafficAsync(ids.Normal, true, default))!;
        Assert.Equal("unavailable", sample.State);
        Assert.Null(sample.Counters);
    }

    [Fact]
    public async Task Session_status_uses_one_router_lookup_and_preserves_staff_visibility()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        var router = new FakeRouter();
        router.Online.UnionWith(["maria_santos", "harry_hidden"]);
        await using var db = fixture.NewContext();
        var queries = new PppoeQueries(db, router);

        var staff = await queries.ListAsync(new(), false, default);
        Assert.Equal(1, router.ActiveSessionReads);
        Assert.True(staff.Items.Single(x => x.CustomerId == ids.Normal).SessionConnected);
        Assert.False(staff.Items.Single(x => x.CustomerId == ids.Pending).SessionConnected);
        Assert.DoesNotContain(staff.Items, x => x.CustomerId == ids.ManagerOnly);
        var staffDetail = (await queries.GetAsync(ids.Normal, false, default))!.Session!;
        Assert.True(staffDetail.Connected);
        Assert.Null(staffDetail.DisconnectReference);
        Assert.True((await queries.ListAsync(new(), true, default)).Items.Single(x => x.CustomerId == ids.ManagerOnly).SessionConnected);

        router.Down = true;
        Assert.All((await queries.ListAsync(new(), false, default)).Items, x => Assert.Null(x.SessionConnected));
        Assert.Null((await queries.GetAsync(ids.Normal, true, default))!.Session!.Connected);
    }

    [Fact]
    public async Task Session_filters_apply_before_paging_and_never_treat_unavailable_as_offline()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        var router = new FakeRouter();
        router.Online.UnionWith(["zzz_online", "harry_hidden", "MARIA_SANTOS"]);
        await using var db = fixture.NewContext();
        for (var i = 1; i <= 12; i++)
        {
            var username = i == 12 ? "zzz_online" : $"account_{i:00}";
            db.Customers.Add(fixture.Make("Fixture", "Account", username, "Fiber 100 Mbps", "NAP-01",
                CustomerStatus.Active, false, 5));
        }
        await db.SaveChangesAsync();
        var queries = new PppoeQueries(db, router);

        var online = await queries.ListAsync(new(Session: "online"), false, default);
        Assert.Equal("zzz_online", Assert.Single(online.Items).PppoeUsername);
        Assert.True(online.Items[0].SessionConnected);
        Assert.Equal(1, online.TotalMatching);
        Assert.Equal(1, router.ActiveSessionReads);

        var offline = await queries.ListAsync(new(Page: 2, Session: "offline"), false, default);
        Assert.Equal(14, offline.TotalMatching);
        Assert.Equal(2, offline.Page);
        Assert.All(offline.Items, row => Assert.False(row.SessionConnected));
        Assert.Contains(offline.Items, row => row.CustomerId == ids.Normal);
        Assert.DoesNotContain(offline.Items, row => row.CustomerId == ids.ManagerOnly);

        router.Down = true;
        var unavailable = await queries.ListAsync(new(Session: "offline"), false, default);
        Assert.True(unavailable.SessionFilterUnavailable);
        Assert.Empty(unavailable.Items);
    }

    [Fact]
    public async Task Manager_disconnects_only_the_confirmed_session_and_records_the_action()
    {
        using var fixture = new TestDb();
        var ids = fixture.Seed();
        var router = new FakeRouter();
        router.Add("maria_santos", "Fiber 100 Mbps", password: "Fixture#Password");
        router.Online.UnionWith(["maria_santos", "harry_hidden"]);
        await using var db = fixture.NewContext();
        var queries = new PppoeQueries(db, router);
        var commands = new PppoeSessionCommands(db, router, fixture.Notifier, NullLogger<PppoeSessionCommands>.Instance);
        var reference = (await queries.GetAsync(ids.Normal, true, default))!.Session!.DisconnectReference;

        Assert.Equal(DisconnectPppoeOutcome.Forbidden, (await commands.DisconnectAsync(ids.Normal, new(reference), TestDb.Staff, default)).Outcome);
        Assert.Equal(DisconnectPppoeOutcome.SessionChanged, (await commands.DisconnectAsync(ids.Normal, new("Mock:old:session"), TestDb.Manager, default)).Outcome);
        Assert.Empty(fixture.Notifier.Disconnects);
        Assert.Equal(0, router.Disconnects);

        Assert.Equal(DisconnectPppoeOutcome.Disconnected, (await commands.DisconnectAsync(ids.Normal, new(reference), TestDb.Manager, default)).Outcome);
        Assert.Equal(DisconnectPppoeOutcome.SessionChanged, (await commands.DisconnectAsync(ids.Normal, new(reference), TestDb.Manager, default)).Outcome);
        Assert.Equal(1, router.Disconnects);
        Assert.False(Assert.Single(router.Secrets).Disabled);
        Assert.Equal(0, router.Updates + router.Deletes);
        Assert.Contains("harry_hidden", router.Online);
        Assert.Equal(CustomerStatus.Active, (await db.Customers.SingleAsync(c => c.Id == ids.Normal)).Status);

        var activity = Assert.Single(await db.CustomerActivities.ToListAsync());
        Assert.Equal("PppoeSessionDisconnected", activity.Action);
        Assert.Equal(TestDb.Manager.UserId, activity.ActorUserId);
        Assert.Equal("Completed", activity.Outcome);
        Assert.Equal(ids.Normal, Assert.Single(fixture.Notifier.Disconnects).CustomerId);
    }

    [Fact]
    public async Task Disconnect_email_goes_to_active_managers_and_excludes_credentials()
    {
        using var host = new IdentityTestHost();
        await host.AddUserAsync("first", Roles.Manager, "first@example.test");
        await host.AddUserAsync("second", Roles.Manager, "second@example.test");
        await host.AddUserAsync("disabled", Roles.Manager, "disabled@example.test", active: false);
        await host.AddUserAsync("staff", Roles.Staff, "staff@example.test");
        await using var scope = host.Scope();
        var queue = new ApprovalEmailNotifier(NullLogger<ApprovalEmailNotifier>.Instance);
        using var dispatcher = new ApprovalEmailDispatcher(queue, scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ApprovalEmailDispatcher>.Instance, new TestDb().Passwords);
        var item = new QueuedApprovalNotice(ApprovalNoticeKind.PppoeSessionDisconnected, null,
            Disconnect: new PppoeDisconnectNotice(1, "fixture_pppoe", "first", "Mock", DateTimeOffset.UtcNow));

        Assert.Equal(new[] { "first@example.test", "second@example.test" },
            (await dispatcher.RecipientsAsync(item, default)).OrderBy(x => x));
        var email = ApprovalEmailDispatcher.Compose(item, new EmailOptions());
        Assert.Contains("fixture_pppoe", email.Body);
        Assert.Contains("mock router", email.Body);
        Assert.DoesNotContain("password", email.Body, StringComparison.OrdinalIgnoreCase);
    }
}
