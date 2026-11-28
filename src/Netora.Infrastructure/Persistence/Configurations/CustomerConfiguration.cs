using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Netora.Domain.Customers;

namespace Netora.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasQueryFilter(c => c.DeletedAtUtc == null);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.LastName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.MobileNumber).HasMaxLength(30);
        builder.Property(c => c.Address).HasMaxLength(300);
        builder.Property(c => c.PppoeUsername).HasMaxLength(40).IsRequired();
        builder.HasOne(c => c.Plan).WithMany().HasForeignKey(c => c.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.Nap).WithMany().HasForeignKey(c => c.NapBoxId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(c => c.OnuSerial).HasMaxLength(50);
        builder.Property(c => c.OnuModel).HasMaxLength(100);
        builder.Property(c => c.RouterSecretId).HasMaxLength(20);
        builder.Property(c => c.Email).HasMaxLength(256);
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.Property(c => c.CreatedByUserId).HasMaxLength(450);
        builder.Property(c => c.UpdatedByUserId).HasMaxLength(450);
        builder.Property(c => c.PppoePasswordProtected).HasMaxLength(1000);
        builder.Property(c => c.PppoeRemoteAddress).HasMaxLength(45);
        builder.Property(c => c.PppoeLocalAddress).HasMaxLength(45);
        builder.Property(c => c.PppoeComment).HasMaxLength(200);
        builder.Property(c => c.RouterSyncStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.ConcurrencyStamp).HasMaxLength(32).IsRequired().IsConcurrencyToken();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(c => c.PppoeUsername).IsUnique().HasFilter("[DeletedAtUtc] IS NULL");
        builder.HasIndex(c => new { c.IsManagerOnly, c.Status });
    }
}
