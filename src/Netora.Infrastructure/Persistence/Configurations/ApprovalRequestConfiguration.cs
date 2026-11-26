using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Netora.Domain.Approvals;
using Netora.Domain.Customers;

namespace Netora.Infrastructure.Persistence.Configurations;

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.ToTable("ApprovalRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Operation).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();
        builder.Property(r => r.Reason).HasMaxLength(500);
        builder.Property(r => r.RequestedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(r => r.RequestedByUserName).HasMaxLength(256).IsRequired();
        builder.Property(r => r.DecidedByUserId).HasMaxLength(450);
        builder.Property(r => r.DecidedByUserName).HasMaxLength(256);
        builder.Property(r => r.DecisionNote).HasMaxLength(500);
        builder.Property(r => r.Before).HasMaxLength(300);
        builder.Property(r => r.After).HasMaxLength(300);
        builder.Property(r => r.TargetPlanName).HasMaxLength(100);
        builder.Property(r => r.ProvisioningStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.ProvisioningError).HasMaxLength(500);

        builder.HasOne<Customer>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Domain.Plans.InternetPlan>().WithMany().HasForeignKey(r => r.TargetPlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.Status, r.RequestedAtUtc });

        // One open request per customer, so the same change cannot be requested (or approved) twice.
        builder.HasIndex(r => r.CustomerId).IsUnique().HasFilter("[Status] = 'Pending'").HasDatabaseName("IX_ApprovalRequests_OnePendingPerCustomer");
    }
}
