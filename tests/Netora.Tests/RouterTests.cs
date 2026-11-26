using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Netora.Application.Approvals;
using Netora.Application.Router;
using Netora.Application.Settings;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;
using Netora.Infrastructure.Pppoe;
using Netora.Infrastructure.Router;

namespace Netora.Tests;

/// <summary>In-memory router for tests. Counts writes and can be told to be unreachable.</summary>
public sealed class FakeRouter : IRouterClient
{
    public List<RouterSecret> Secrets { get; } = [];
    public int Creates { get; private set; }
    public int Updates { get; private set; }
    public bool Down { get; set; }
    public HashSet<string> Online { get; } = [];
    private int next = 1;

    public string Mode => "Mock";

    public Task<RouterIdentity> GetIdentityAsync(CancellationToken ct) => Down
        ? throw new RouterException(RouterErrorKind.Unreachable, "down")
        : Task.FromResult(new RouterIdentity("Test", "7.16", null));

    public Task<IReadOnlyList<RouterSecret>> ListSecretsAsync(CancellationToken ct)
    {
        Check();
        return Task.FromResult<IReadOnlyList<RouterSecret>>(Secrets.ToList());
    }

    public Task<RouterSecret?> FindSecretAsync(string? id, string name, CancellationToken ct)
    {
        Check();
        return Task.FromResult((id is null ? null : Secrets.FirstOrDefault(s => s.Id == id)) ?? Secrets.FirstOrDefault(s => s.Name == name));
    }

    public Task<RouterSecret> CreateSecretAsync(RouterSecretWrite w, CancellationToken ct)
    {
        Check();
        Creates++;
        var s = new RouterSecret($"*{next++:X}", w.Name, w.Password, w.Profile, w.Disabled, w.Comment, w.RemoteAddress, w.LocalAddress, "pppoe");
        Secrets.Add(s);
        return Task.FromResult(s);
    }

    public Task<RouterSecret> UpdateSecretAsync(string id, RouterSecretWrite w, CancellationToken ct)
    {
        Check();
        Updates++;
        var i = Secrets.FindIndex(s => s.Id == id);
        var old = Secrets[i];
        Secrets[i] = old with { Name = w.Name, Password = w.Password ?? old.Password, Profile = w.Profile, Disabled = w.Disabled, Comment = w.Comment };
        return Task.FromResult(Secrets[i]);
    }

    public Task<RouterActiveSession?> FindActiveSessionAsync(string name, CancellationToken ct)
        => Task.FromResult(Online.Contains(name) ? new RouterActiveSession(name, "10.0.0.9", "1h", null) : null);

    public List<string> Profiles { get; } = ["default"];

    public Task<IReadOnlyList<string>> ListProfilesAsync(CancellationToken ct)
    {
        Check();
        return Task.FromResult<IReadOnlyList<string>>(Profiles.ToList());
    }

    public RouterSecret Add(string name, string profile, bool disabled = false, string? comment = null, string? password = "pw")
    {
        var s = new RouterSecret($"*{next++:X}", name, password, profile, disabled, comment, null, null, "pppoe");
        Secrets.Add(s);
        return s;
    }

    private void Check()
    {
        if (Down) throw new RouterException(RouterErrorKind.Unreachable, "The router at test:443 could not be reached.");
    }
}

public class RouterTests
{
    private static readonly IDataProtectionProvider Keys = new EphemeralDataProtectionProvider();

    private static RouterProvisioner Provisioner(NetoraDbContext db, FakeRouter router)
        => new(db, router, new PppoePasswordProtector(Keys), NullLogger<RouterProvisioner>.Instance);

    private static async Task<long> ApprovedAsync(TestDb t, int customerId, ApprovalOperation op, Action<Customer>? change = null)
    {
        await using var db = t.NewContext();
        var c = await db.Customers.SingleAsync(x => x.Id == customerId);
        change?.Invoke(c);
        var r = new ApprovalRequest
        {
            CustomerId = customerId, Operation = op, Status = ApprovalStatus.Approved, ProvisioningStatus = ProvisioningStatus.NotSent,
            RequestedByUserId = "staff-id", RequestedByUserName = "staff01", RequestedAtUtc = DateTimeOffset.UtcNow,
        };
        db.ApprovalRequests.Add(r);
        await db.SaveChangesAsync();
        return r.Id;
    }

    private static async Task<(ApprovalRequest Request, Customer Customer)> LoadAsync(TestDb t, long requestId)
    {
        await using var db = t.NewContext();
        var r = await db.ApprovalRequests.SingleAsync(x => x.Id == requestId);
        return (r, await db.Customers.SingleAsync(x => x.Id == r.CustomerId));
    }

    private static async Task<ProvisioningResult> ProvisionAsync(TestDb t, FakeRouter router, long id)
    {
        await using var db = t.NewContext();
        return await Provisioner(db, router).ProvisionAsync(id, "manager-id", "manager01", default);
    }

    private static void SetPassword(Customer c) => c.PppoePasswordProtected = new PppoePasswordProtector(Keys).Protect("Secret#123");

    // ----- 04.02 create after approval -----

    [Fact]
    public async Task Approved_creation_creates_the_secret_once_and_marks_it_synced()
    {
        var t = new TestDb(); var ids = t.Seed();
        var router = new FakeRouter();
        var id = await ApprovedAsync(t, ids.Pending, ApprovalOperation.CreatePppoe, c => { SetPassword(c); c.Status = CustomerStatus.Active; });

        var first = await ProvisionAsync(t, router, id);
        var again = await ProvisionAsync(t, router, id);

        Assert.Equal("Succeeded", first.Status);
        Assert.Equal("Succeeded", again.Status);
        Assert.Equal(1, router.Creates);
        Assert.Equal(0, router.Updates);
        var secret = Assert.Single(router.Secrets);
        Assert.Equal("pia_pending", secret.Name);
        Assert.Equal("Secret#123", secret.Password);
        Assert.False(secret.Disabled);
        Assert.Contains(RouterRules.Marker(ids.Pending), secret.Comment);
        var (r, c2) = await LoadAsync(t, id);
        Assert.Equal(1, r.ProvisioningAttempts);
        Assert.Equal(RouterSyncStatus.Synced, c2.RouterSyncStatus);
        Assert.Equal(secret.Id, c2.RouterSecretId);
    }

    [Fact]
    public async Task Creation_never_overwrites_a_secret_netora_did_not_create()
    {
        var t = new TestDb(); var ids = t.Seed();
        var router = new FakeRouter();
        var foreign = router.Add("pia_pending", "Old Plan", comment: "someone else");
        var id = await ApprovedAsync(t, ids.Pending, ApprovalOperation.CreatePppoe, c => { SetPassword(c); c.Status = CustomerStatus.Active; });

        var result = await ProvisionAsync(t, router, id);

        Assert.Equal("Failed", result.Status);
        Assert.Contains("was not created by Netora", result.Error);
        Assert.Equal(foreign, router.Secrets.Single());
        var (_, c) = await LoadAsync(t, id);
        Assert.Equal(RouterSyncStatus.Failed, c.RouterSyncStatus);
    }

    [Fact]
    public async Task A_retry_adopts_the_secret_an_earlier_attempt_created()
    {
        var t = new TestDb(); var ids = t.Seed();
        var router = new FakeRouter();
        router.Add("pia_pending", "Fiber 50 Mbps", comment: RouterRules.Marker(ids.Pending));
        var id = await ApprovedAsync(t, ids.Pending, ApprovalOperation.CreatePppoe, c => { SetPassword(c); c.Status = CustomerStatus.Active; });

        var result = await ProvisionAsync(t, router, id);

        Assert.Equal("Succeeded", result.Status);
        Assert.Equal(0, router.Creates);
        Assert.Single(router.Secrets);
    }

    // ----- 04.03 and 04.05 -----

    [Fact]
    public async Task Disable_and_terminate_disable_the_secret_and_never_delete_it()
    {
        var t = new TestDb(); var ids = t.Seed();
        var router = new FakeRouter();
        router.Add("maria_santos", "Fiber 100 Mbps");
        var disable = await ApprovedAsync(t, ids.Normal, ApprovalOperation.DisablePppoe, c => c.Status = CustomerStatus.Disabled);
        Assert.Equal("Succeeded", (await ProvisionAsync(t, router, disable)).Status);
        Assert.True(router.Secrets.Single().Disabled);

        var terminate = await ApprovedAsync(t, ids.Normal, ApprovalOperation.TerminatePppoe, c => c.Status = CustomerStatus.Terminated);
        Assert.Equal("Succeeded", (await ProvisionAsync(t, router, terminate)).Status);
        Assert.Single(router.Secrets);
        Assert.True(router.Secrets.Single().Disabled);
    }

    [Fact]
    public async Task Plan_change_sets_the_profile()
    {
        var t = new TestDb(); var ids = t.Seed();
        var router = new FakeRouter();
        router.Add("maria_santos", "Fiber 100 Mbps");
        var id = await ApprovedAsync(t, ids.Normal, ApprovalOperation.ChangePlan, c => c.PlanId = t.PlanIds["Fiber 300 Mbps"]);

        Assert.Equal("Succeeded", (await ProvisionAsync(t, router, id)).Status);
        Assert.Equal("Fiber 300 Mbps", router.Secrets.Single().Profile);
    }

    [Fact]
    public async Task Router_failures_are_recorded_and_retried_until_they_succeed_then_never_again()
    {
        var t = new TestDb(); var ids = t.Seed();
        var router = new FakeRouter { Down = true };
        router.Secrets.Add(new RouterSecret("*99", "maria_santos", "pw", "Fiber 100 Mbps", false, null, null, null, "pppoe"));
        var id = await ApprovedAsync(t, ids.Normal, ApprovalOperation.DisablePppoe, c => c.Status = CustomerStatus.Disabled);

        var failed = await ProvisionAsync(t, router, id);
        Assert.Equal("Failed", failed.Status);
        Assert.Contains("could not be reached", failed.Error);
        await using (var db = t.NewContext())
            Assert.True(ApprovalCommands.CanRetry(await db.ApprovalRequests.SingleAsync(r => r.Id == id)));

        router.Down = false;
        var retried = await ProvisionAsync(t, router, id);
        Assert.Equal("Succeeded", retried.Status);
        Assert.Equal(2, retried.Attempts);
        await using (var db = t.NewContext())
        {
            var r = await db.ApprovalRequests.SingleAsync(x => x.Id == id);
            Assert.False(ApprovalCommands.CanRetry(r));
            Assert.Null(r.ProvisioningError);
            Assert.Equal(2, await db.CustomerActivities.CountAsync(a => a.CustomerId == ids.Normal && a.Action.StartsWith("RouterProvision")));
        }
    }

    [Fact]
    public async Task Pending_and_rejected_requests_are_not_sent()
    {
        var t = new TestDb(); var ids = t.Seed();
        var router = new FakeRouter();
        await using (var db = t.NewContext())
        {
            db.ApprovalRequests.Add(new ApprovalRequest
            {
                CustomerId = ids.Normal, Operation = ApprovalOperation.DisablePppoe, Status = ApprovalStatus.Rejected,
                RequestedByUserId = "x", RequestedByUserName = "x", RequestedAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        long rejected;
        await using (var db = t.NewContext()) rejected = (await db.ApprovalRequests.SingleAsync()).Id;

        var result = await ProvisionAsync(t, router, rejected);

        Assert.Equal("NotApplicable", result.Status);
        Assert.Equal(0, router.Creates + router.Updates);
    }

    [Fact]
    public async Task Approving_a_staff_request_sends_it_to_the_router()
    {
        var t = new TestDb(); var ids = t.Seed();
        await using (var db = t.NewContext())
            await t.PppoeCommands(db).SubmitAsync(ids.Normal, new Netora.Application.Pppoe.SubmitPppoeRequest("Disable", null), TestDb.Staff, default);
        Assert.Empty(t.Provisioner.Calls);

        long id;
        await using (var db = t.NewContext()) id = (await db.ApprovalRequests.SingleAsync()).Id;
        await using (var db = t.NewContext())
            await t.PppoeCommands(db).DecideAsync(ids.Normal, id, new Netora.Application.Pppoe.DecidePppoeRequest(true, null), TestDb.Manager, default);

        Assert.Equal([id], t.Provisioner.Calls);
    }

    // ----- 09.03 / 09.04 status and mismatch -----

    [Fact]
    public async Task Status_reports_mismatches_without_changing_anything()
    {
        var t = new TestDb(); var ids = t.Seed();
        var router = new FakeRouter();
        router.Add("maria_santos", "Fiber 30 Mbps", disabled: true);
        router.Online.Add("maria_santos");

        await using var db = t.NewContext();
        var status = await new RouterStatusService(db, router).CheckAsync(ids.Normal, viewerIsManager: false, default);

        Assert.NotNull(status);
        Assert.True(status!.Reachable);
        Assert.True(status.SecretFound);
        Assert.Equal("Disabled", status.SecretState);
        Assert.Contains(status.Mismatches, m => m.Field == "Profile (Internet Plan)" && m.Expected == "Fiber 100 Mbps" && m.Actual == "Fiber 30 Mbps");
        Assert.Contains(status.Mismatches, m => m.Field == "Secret state" && m.Expected == "Enabled");
        Assert.Equal(0, router.Updates);
        Assert.False(status.SessionActive);   // a disabled secret has no session check
    }

    [Fact]
    public async Task Status_flags_missing_secrets_hides_manager_only_and_survives_a_dead_router()
    {
        var t = new TestDb(); var ids = t.Seed();
        var router = new FakeRouter();
        await using var db = t.NewContext();
        var service = new RouterStatusService(db, router);

        var missing = await service.CheckAsync(ids.Normal, true, default);
        var pending = await service.CheckAsync(ids.Pending, true, default);
        var hidden = await service.CheckAsync(ids.ManagerOnly, viewerIsManager: false, default);
        router.Down = true;
        var down = await service.CheckAsync(ids.Normal, true, default);

        Assert.Contains(missing!.Mismatches, m => m.Field == "PPPoE secret" && m.Actual == "Missing on the router");
        Assert.Empty(pending!.Mismatches);
        Assert.Null(hidden);
        Assert.False(down!.Reachable);
        Assert.Contains("could not be reached", down.Error);
    }

    // ----- 04.04 import -----

    [Fact]
    public async Task Import_scans_on_demand_skips_existing_usernames_and_imports_with_unknown_nap()
    {
        var t = new TestDb(); var ids = t.Seed();
        var router = new FakeRouter();
        router.Add("maria_santos", "Fiber 100 Mbps");
        router.Add("rosa_new", "Fiber 50 Mbps", disabled: true, comment: "Rosa Dela Cruz");
        router.Add("bad name!", "x");

        await using (var db = t.NewContext())
        {
            var scan = await new PppoeImportService(db, router, new PppoePasswordProtector(Keys), NullLogger<PppoeImportService>.Instance).ScanAsync(default);
            Assert.Equal(1, scan.NewCount);
            Assert.Equal("Already imported", scan.Items.Single(i => i.Name == "maria_santos").State);
            Assert.Equal("Cannot import", scan.Items.Single(i => i.Name == "bad name!").State);
            Assert.Equal(0, await db.Customers.CountAsync(c => c.PppoeUsername == "rosa_new"));
        }

        await using (var db = t.NewContext())
        {
            var import = new PppoeImportService(db, router, new PppoePasswordProtector(Keys), NullLogger<PppoeImportService>.Instance);
            var result = await import.ImportAsync(new ImportRequest(["rosa_new", "maria_santos"]), "manager-id", "manager01", default);
            Assert.Equal(1, result.Imported);
            Assert.Equal(1, result.Skipped);
        }

        await using (var db = t.NewContext())
        {
            var c = await db.Customers.SingleAsync(x => x.PppoeUsername == "rosa_new");
            Assert.Equal(t.NapIds[Customer.UnknownNap], c.NapBoxId);
            Assert.Equal(t.PlanIds["Fiber 50 Mbps"], c.PlanId);   // the plan whose profile the secret uses
            Assert.Equal(CustomerStatus.Disabled, c.Status);
            Assert.Equal(("Rosa", "Dela Cruz"), (c.FirstName, c.LastName));
            Assert.Equal(router.Secrets.Single(s => s.Name == "rosa_new").Id, c.RouterSecretId);
            Assert.Equal("pw", new PppoePasswordProtector(Keys).Unprotect(c.PppoePasswordProtected!));
            var again = await new PppoeImportService(db, router, new PppoePasswordProtector(Keys), NullLogger<PppoeImportService>.Instance)
                .ImportAsync(new ImportRequest(["rosa_new"]), "manager-id", "manager01", default);
            Assert.Equal(0, again.Imported);
        }
        Assert.Equal(0, router.Updates + router.Creates);
    }

    // ----- helpers -----

    [Theory]
    [InlineData("Home router [netora:NET-0005]", "Home router")]
    [InlineData("[netora:NET-0005]", null)]
    [InlineData(null, null)]
    public void Marker_is_removed_from_comments(string? comment, string? expected)
        => Assert.Equal(expected, RouterRules.StripMarker(comment));

    [Theory]
    [InlineData("AB:CD", false)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
    [InlineData("01 23 45 67 89 AB CD EF 01 23 45 67 89 AB CD EF 01 23 45 67 89 AB CD EF 01 23 45 67 89 AB CD EF", true)]
    public void Certificate_fingerprint_must_be_sha256(string value, bool valid)
    {
        var result = new UpdateRouterSettingsRequestValidator().Validate(new UpdateRouterSettingsRequest("r", "10.0.0.1", 443, "u", null, false, value));
        Assert.Equal(valid, result.IsValid);
    }
}
