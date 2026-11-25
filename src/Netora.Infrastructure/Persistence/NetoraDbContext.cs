using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;
using Netora.Domain.Security;
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NetoraDbContext).Assembly);

        modelBuilder.Entity<ApplicationUser>(b =>
        {
            b.Property(u => u.DisplayName).HasMaxLength(200);
        });
    }
}
