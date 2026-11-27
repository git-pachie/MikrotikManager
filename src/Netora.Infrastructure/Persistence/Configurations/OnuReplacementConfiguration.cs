using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Netora.Domain.Customers;

namespace Netora.Infrastructure.Persistence.Configurations;

internal sealed class OnuReplacementConfiguration : IEntityTypeConfiguration<OnuReplacement>
{
    public void Configure(EntityTypeBuilder<OnuReplacement> builder)
    {
        builder.HasOne<Customer>().WithMany().HasForeignKey(r => r.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.PreviousSerial).HasMaxLength(50);
        builder.Property(r => r.NewSerial).HasMaxLength(50);
        builder.Property(r => r.PreviousModel).HasMaxLength(100);
        builder.Property(r => r.NewModel).HasMaxLength(100);
        builder.Property(r => r.Reason).HasMaxLength(500);
        builder.Property(r => r.OperatorId).HasMaxLength(450);
        builder.Property(r => r.Operator).HasMaxLength(256);
        builder.HasIndex(r => new { r.CustomerId, r.ReplacedOn });
        builder.HasIndex(r => new { r.ReplacedOn, r.Id });
    }
}
