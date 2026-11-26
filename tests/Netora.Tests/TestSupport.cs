using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Netora.Application.Customers;
using Netora.Application.Customers.Attachments;
using Netora.Domain.Customers;
using Netora.Infrastructure.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Tests;

/// <summary>
/// Builds an isolated in-memory database per test. The in-memory provider has no unique indexes or real
/// transactions, so duplicate-race behaviour is covered by the explicit existence check only.
/// </summary>
/// <summary>Records approval notices instead of emailing them. Can be told to throw, to prove emails never change outcomes.</summary>
public sealed class RecordingNotifier : Netora.Application.Approvals.IApprovalNotifier
{
    public List<Netora.Application.Approvals.ApprovalNotice> Submitted { get; } = [];
    public List<Netora.Application.Approvals.ApprovalNotice> Decided { get; } = [];
    public bool Throw { get; set; }

    public void RequestSubmitted(Netora.Application.Approvals.ApprovalNotice notice)
    {
        if (Throw) throw new InvalidOperationException("SMTP down");
        Submitted.Add(notice);
    }

    public void RequestDecided(Netora.Application.Approvals.ApprovalNotice notice)
    {
        if (Throw) throw new InvalidOperationException("SMTP down");
        Decided.Add(notice);
    }
}

public sealed class RecordingProvisioner : Netora.Application.Router.IRouterProvisioner
{
    public List<long> Calls { get; } = [];

    public Task<Netora.Application.Router.ProvisioningResult> ProvisionAsync(long requestId, string actorUserId, string actorUserName, CancellationToken cancellationToken)
    {
        Calls.Add(requestId);
        return Task.FromResult(new Netora.Application.Router.ProvisioningResult("NotSent", null, 0));
    }
}

public sealed class TestDb : IDisposable
{
    private readonly DbContextOptions<NetoraDbContext> options;

    public TestDb() : this([]) { }

    /// <param name="interceptors">For example the file log interceptor (module 18).</param>
    public TestDb(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors)
    {
        options = new DbContextOptionsBuilder<NetoraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(interceptors)
            .Options;

        // The catalogue every test can rely on (modules 05 and 06): profile = plan name, as the migration sets it.
        using var db = NewContext();
        var now = DateTimeOffset.UtcNow;
        foreach (var name in CatalogPlans)
            db.InternetPlans.Add(new Netora.Domain.Plans.InternetPlan { Name = name, RouterProfile = name, CreatedAtUtc = now });
        db.NapBoxes.Add(new Netora.Domain.Network.NapBox { Code = Customer.UnknownNap, IsSystem = true, CreatedAtUtc = now });
        foreach (var code in CatalogNaps)
            db.NapBoxes.Add(new Netora.Domain.Network.NapBox { Code = code, CreatedAtUtc = now });
        db.SaveChanges();
        PlanIds = db.InternetPlans.ToDictionary(p => p.Name, p => p.Id);
        NapIds = db.NapBoxes.ToDictionary(n => n.Code, n => n.Id);
    }

    public static readonly string[] CatalogPlans = ["Fiber 30 Mbps", "Fiber 50 Mbps", "Fiber 100 Mbps", "Fiber 300 Mbps"];
    public static readonly string[] CatalogNaps = ["NAP-01", "NAP-02", "NAP-03"];

    /// <summary>Plan IDs by name.</summary>
    public IReadOnlyDictionary<string, int> PlanIds { get; }

    /// <summary>NAP box IDs by code, including UNKNOWN.</summary>
    public IReadOnlyDictionary<string, int> NapIds { get; }

    public NetoraDbContext NewContext() => new(options);

    internal CustomerQueries Queries(NetoraDbContext db) => new(db, Microsoft.Extensions.Options.Options.Create(new AttachmentOptions()));

    /// <summary>Approval notices sent by the commands built from this database.</summary>
    public RecordingNotifier Notifier { get; } = new();

    internal CustomerCommands Commands(NetoraDbContext db) => new(
        new CreateCustomerRequestValidator(),
        new UpdateCustomerRequestValidator(),
        new CustomerRepository(db),
        Notifier,
        NullLogger<CustomerCommands>.Instance);

    internal Netora.Application.Pppoe.PppoeCommands PppoeCommands(NetoraDbContext db) => new(
        new Netora.Application.Pppoe.UpdatePppoeRequestValidator(),
        new CustomerRepository(db),
        new Netora.Infrastructure.Pppoe.ApprovalRepository(db),
        new Netora.Infrastructure.Pppoe.PppoePasswordProtector(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()),
        Notifier,
        Provisioner,
        NullLogger<Netora.Application.Pppoe.PppoeCommands>.Instance);

    /// <summary>Router stand-in for the approval tests: records the calls and leaves requests "NotSent".</summary>
    public RecordingProvisioner Provisioner { get; } = new();

    public static readonly CustomerActor Manager = new("manager-id", "manager01", IsManager: true);
    public static readonly CustomerActor Staff = new("staff-id", "staff01", IsManager: false);

    /// <summary>Four customers: a normal Active one, a Manager-only Active one, a Pending one and a Terminated one.</summary>
    public (int Normal, int ManagerOnly, int Pending, int Terminated) Seed()
    {
        using var db = NewContext();
        var normal = Make("Maria", "Santos", "maria_santos", "Fiber 100 Mbps", "NAP-01", CustomerStatus.Active, false, 1);
        var restricted = Make("Harry", "Hidden", "harry_hidden", "Fiber 50 Mbps", "NAP-02", CustomerStatus.Active, true, 2);
        var pending = Make("Pia", "Pending", "pia_pending", "Fiber 30 Mbps", "UNKNOWN", CustomerStatus.PendingApproval, false, 3);
        var terminated = Make("Tom", "Terminated", "tom_term", "Fiber 30 Mbps", "NAP-01", CustomerStatus.Terminated, false, 4);
        db.Customers.AddRange(normal, restricted, pending, terminated);
        db.SaveChanges();
        return (normal.Id, restricted.Id, pending.Id, terminated.Id);
    }

    /// <summary>A customer on a catalogue plan and NAP box (linked by ID).</summary>
    public Customer Make(string first, string last, string username, string plan, string nap,
        CustomerStatus status, bool managerOnly, int createdDayOffset) => new()
    {
        FirstName = first,
        LastName = last,
        PppoeUsername = username,
        PlanId = PlanIds[plan],
        NapBoxId = NapIds[nap],
        Address = "Test address",
        Status = status,
        IsManagerOnly = managerOnly,
        CreatedAtUtc = new DateTimeOffset(2026, 10, createdDayOffset, 9, 0, 0, TimeSpan.Zero),
    };

    public static CreateCustomerRequest NewCustomer(string username = "new_user", bool managerOnly = false) => new(
        "Ana", "Reyes", "0917 555 0101", "ana@example.com", "1 Sample St", null, username, "Fiber 50 Mbps", "NAP-01",
        null, null, managerOnly);

    public static UpdateCustomerRequest EditOf(CustomerDetailDto d, Func<UpdateCustomerRequest, UpdateCustomerRequest>? change = null)
    {
        var request = new UpdateCustomerRequest(
            d.FirstName, d.LastName, d.MobileNumber, d.Email, d.Address ?? "Test address", d.Notes, d.PppoeUsername,
            d.PlanName, d.NapBox, d.OnuSerial, d.OnuModel, d.IsManagerOnly, d.ConcurrencyStamp);
        return change is null ? request : change(request);
    }

    public void Dispose() { }
}
