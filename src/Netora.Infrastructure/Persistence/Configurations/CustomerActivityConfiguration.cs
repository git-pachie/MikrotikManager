using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Netora.Domain.Customers;

namespace Netora.Infrastructure.Persistence.Configurations;

internal sealed class CustomerActivityConfiguration : IEntityTypeConfiguration<CustomerActivity>
{
    public void Configure(EntityTypeBuilder<CustomerActivity> builder)
    {
        builder.ToTable("CustomerActivities");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasMaxLength(60).IsRequired();
        builder.Property(a => a.Detail).HasMaxLength(500);
        builder.Property(a => a.ActorUserId).HasMaxLength(450).IsRequired();
        builder.Property(a => a.ActorUserName).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Outcome).HasMaxLength(30).IsRequired();

        // History is retained, so deleting a customer row must never cascade into it (there is no customer delete).
        builder.HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(a => new { a.CustomerId, a.OccurredAtUtc });
    }
}
