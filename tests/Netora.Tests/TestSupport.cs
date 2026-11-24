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
public sealed class TestDb : IDisposable
{
    private readonly DbContextOptions<NetoraDbContext> options;

    public TestDb()
    {
        options = new DbContextOptionsBuilder<NetoraDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
    }

    public NetoraDbContext NewContext() => new(options);

    internal CustomerQueries Queries(NetoraDbContext db) => new(db, Microsoft.Extensions.Options.Options.Create(new AttachmentOptions()));

    internal CustomerCommands Commands(NetoraDbContext db) => new(
        new CreateCustomerRequestValidator(),
        new UpdateCustomerRequestValidator(),
        new CustomerRepository(db),
        NullLogger<CustomerCommands>.Instance);

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

    public static Customer Make(string first, string last, string username, string plan, string nap,
        CustomerStatus status, bool managerOnly, int createdDayOffset) => new()
    {
        FirstName = first,
        LastName = last,
        PppoeUsername = username,
        PlanName = plan,
        NapBox = nap,
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
