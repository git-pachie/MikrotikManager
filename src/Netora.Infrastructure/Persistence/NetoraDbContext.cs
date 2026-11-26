using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Domain.Security;
using Netora.Domain.Settings;
using Netora.Infrastructure.Identity;

namespace Netora.Infrastructure.Persistence;

public sealed class NetoraDbContext(DbContextOptions<NetoraDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerActivity> CustomerActivities => Set<CustomerActivity>();
    public DbSet<CustomerAttachment> CustomerAttachments => Set<CustomerAttachment>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<Domain.Plans.InternetPlan> InternetPlans => Set<Domain.Plans.InternetPlan>();
    public DbSet<Domain.Network.NapBox> NapBoxes => Set<Domain.Network.NapBox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NetoraDbContext).Assembly);

        modelBuilder.Entity<ApplicationUser>(b =>
        {
            b.Property(u => u.DisplayName).HasMaxLength(200);
            b.Property(u => u.ThemePreference).HasMaxLength(10);
        });
    }
}
