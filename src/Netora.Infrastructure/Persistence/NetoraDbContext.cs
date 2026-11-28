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
    public DbSet<RevokedAccessToken> RevokedAccessTokens => Set<RevokedAccessToken>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerActivity> CustomerActivities => Set<CustomerActivity>();
    public DbSet<OnuReplacement> OnuReplacements => Set<OnuReplacement>();
    public DbSet<CustomerAttachment> CustomerAttachments => Set<CustomerAttachment>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<ApprovalEmailDelivery> ApprovalEmailDeliveries => Set<ApprovalEmailDelivery>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<BackupRun> BackupRuns => Set<BackupRun>();
    public DbSet<Domain.Plans.InternetPlan> InternetPlans => Set<Domain.Plans.InternetPlan>();
    public DbSet<Domain.Network.NapBox> NapBoxes => Set<Domain.Network.NapBox>();
    public DbSet<Domain.Audit.AuditEvent> AuditEvents => Set<Domain.Audit.AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NetoraDbContext).Assembly);
        modelBuilder.Entity<RevokedAccessToken>().HasIndex(token => token.RetainUntilUtc);
        modelBuilder.Entity<RevokedAccessToken>().Property(token => token.Id).ValueGeneratedNever();
        modelBuilder.Entity<ApprovalEmailDelivery>(b =>
        {
            b.Property(e => e.Kind).HasMaxLength(40);
            b.Property(e => e.Status).HasMaxLength(40);
            b.HasIndex(e => e.OccurredAtUtc);
            b.HasOne<ApprovalRequest>().WithMany().HasForeignKey(e => e.RequestId);
        });
        modelBuilder.Entity<BackupRun>(b =>
        {
            b.Property(r => r.ActorUserId).HasMaxLength(450);
            b.Property(r => r.Status).HasMaxLength(20);
            b.HasIndex(r => new { r.Scheduled, r.StartedAtUtc });
        });

        modelBuilder.Entity<ApplicationUser>(b =>
        {
            b.Property(u => u.DisplayName).HasMaxLength(200);
            b.Property(u => u.ThemePreference).HasMaxLength(10);
        });
    }
}
