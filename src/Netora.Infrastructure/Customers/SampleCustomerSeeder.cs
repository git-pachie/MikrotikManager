using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Netora.Domain.Customers;
using Netora.Infrastructure.Persistence;

namespace Netora.Infrastructure.Customers;

/// <summary>
/// Development-only sample customers (the fictional names from the approved mockup). Inserted only when the
/// Customers table is empty, and flagged <see cref="Customer.IsSampleData"/> so they can be deleted later with
/// <c>DELETE FROM Customers WHERE IsSampleData = 1</c>.
/// </summary>
public static class SampleCustomerSeeder
{
    private static readonly (string Last, string First, string User, string Plan, string Nap, string? Onu, CustomerStatus Status, string Created, bool ManagerOnly)[] Rows =
    [
        ("Dela Cruz", "Juan", "juan_dc", "Fiber 50 Mbps", "NAP-01", "ZTEG8A5F3D21", CustomerStatus.Active, "2026-10-10", false),
        ("Santos", "Maria", "maria_santos", "Fiber 100 Mbps", "NAP-01", "ZTEG8A5F3E19", CustomerStatus.Active, "2026-10-11", false),
        ("Reyes", "Pedro", "pedro_reyes", "Fiber 50 Mbps", "NAP-02", "HWTCA3B9127F", CustomerStatus.Active, "2026-10-12", false),
        ("Garcia", "Ana", "ana_garcia", "Fiber 30 Mbps", "NAP-02", "HWTCA3B9184C", CustomerStatus.Active, "2026-10-12", false),
        ("Torres", "Miguel", "miguel_torres", "Fiber 100 Mbps", "NAP-03", "ZTEG8A5F3E77", CustomerStatus.Active, "2026-10-13", true),
        ("Lim", "Carla", "carla_lim", "Fiber 50 Mbps", "NAP-03", "ZTEG8A5F3F02", CustomerStatus.Active, "2026-10-14", false),
        ("Ramos", "Jose", "jose_ramos", "Fiber 30 Mbps", Customer.UnknownNap, null, CustomerStatus.Active, "2026-10-14", false),
        ("Villanueva", "Rita", "rita_villanueva", "Fiber 50 Mbps", "NAP-04", "HWTCA3B92011", CustomerStatus.Disabled, "2026-10-15", false),
        ("Navarro", "Edwin", "edwin_navarro", "Fiber 30 Mbps", Customer.UnknownNap, null, CustomerStatus.Active, "2026-10-15", false),
        ("Ortiz", "Liza", "liza_ortiz", "Fiber 50 Mbps", "NAP-04", "ZTEG8A5F3F88", CustomerStatus.Terminated, "2026-09-20", false),
        ("Bautista", "Carlo", "carlo_b", "Fiber 100 Mbps", "NAP-01", "ZTEG8A5F3E71", CustomerStatus.Active, "2026-10-17", false),
        ("Mendoza", "Elena", "elena_m", "Fiber 50 Mbps", "NAP-02", "ZTEG8A5F3E72", CustomerStatus.Active, "2026-10-18", false),
        ("Aquino", "Roberto", "roberto_a", "Fiber 30 Mbps", "NAP-03", "HWTCA3B92012", CustomerStatus.Active, "2026-10-19", false),
        ("Cruz", "Nina", "nina_c", "Fiber 100 Mbps", "NAP-04", "ZTEG8A5F3E73", CustomerStatus.Active, "2026-10-20", true),
        ("Flores", "Danilo", "danilo_f", "Fiber 50 Mbps", "NAP-01", "ZTEG8A5F3E74", CustomerStatus.Active, "2026-10-21", false),
        ("Rivera", "Joy", "joy_r", "Fiber 30 Mbps", "NAP-02", "ZTEG8A5F3E75", CustomerStatus.Active, "2026-10-22", false),
        ("Pascual", "Eric", "eric_p", "Fiber 100 Mbps", "NAP-03", "ZTEG8A5F3E76", CustomerStatus.Active, "2026-10-23", false),
        ("Domingo", "Ruth", "ruth_d", "Fiber 50 Mbps", "NAP-04", "ZTEG8A5F3E78", CustomerStatus.Active, "2026-10-24", false),
        ("Castillo", "Mark", "mark_c", "Fiber 30 Mbps", "NAP-01", "ZTEG8A5F3E79", CustomerStatus.Active, "2026-10-25", false),
        ("Valdez", "Mia", "mia_v", "Fiber 50 Mbps", "NAP-02", "ZTEG8A5F3E80", CustomerStatus.Active, "2026-10-26", false),
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NetoraDbContext>();
        if (await db.Customers.IgnoreQueryFilters().AnyAsync(cancellationToken)) return;

        // The plans and NAP boxes the samples use, unless they already exist (UNKNOWN comes from the migration).
        // Plans map to a PPP profile of the same name, as before module 05.
        var now = DateTimeOffset.UtcNow;
        var plans = await db.InternetPlans.ToDictionaryAsync(p => p.Name, cancellationToken);
        foreach (var name in Rows.Select(r => r.Plan).Distinct().Where(p => !plans.ContainsKey(p)))
            db.InternetPlans.Add(plans[name] = new Domain.Plans.InternetPlan { Name = name, RouterProfile = name, CreatedAtUtc = now });
        var naps = await db.NapBoxes.ToDictionaryAsync(x => x.Code, cancellationToken);
        foreach (var code in Rows.Select(r => r.Nap).Distinct().Where(c => !naps.ContainsKey(c)))
            db.NapBoxes.Add(naps[code] = new Domain.Network.NapBox { Code = code, IsSystem = code == Customer.UnknownNap, CreatedAtUtc = now });

        var n = 0;
        foreach (var r in Rows)
        {
            n++;
            db.Customers.Add(new Customer
            {
                FirstName = r.First,
                LastName = r.Last,
                MobileNumber = $"0917 555 {n:0000}",
                Address = $"Sample Street {n}, Sample City",
                PppoeUsername = r.User,
                Plan = plans[r.Plan],
                Nap = naps[r.Nap],
                OnuSerial = r.Onu,
                Status = r.Status,
                IsManagerOnly = r.ManagerOnly,
                IsSampleData = true,
                CreatedAtUtc = DateTimeOffset.Parse(r.Created + "T09:00:00+08:00"),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Netora.SampleCustomerSeeder")
            .LogWarning("Inserted {Count} sample customers (Development only, IsSampleData = 1)", Rows.Length);
    }
}
