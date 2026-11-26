using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Netora.Domain.Network;
using Netora.Domain.Plans;

namespace Netora.Infrastructure.Persistence.Configurations;

internal sealed class InternetPlanConfiguration : IEntityTypeConfiguration<InternetPlan>
{
    public void Configure(EntityTypeBuilder<InternetPlan> builder)
    {
        builder.ToTable("InternetPlans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(100).IsRequired();
        builder.Property(p => p.RouterProfile).HasMaxLength(100).IsRequired();
        builder.Property(p => p.MonthlyFee).HasPrecision(10, 2);
        builder.Property(p => p.CreatedByUserId).HasMaxLength(450);
        builder.Property(p => p.UpdatedByUserId).HasMaxLength(450);
        builder.Property(p => p.ConcurrencyStamp).HasMaxLength(32).IsRequired().IsConcurrencyToken();
        builder.HasIndex(p => p.Name).IsUnique();
    }
}

internal sealed class NapBoxConfiguration : IEntityTypeConfiguration<NapBox>
{
    public void Configure(EntityTypeBuilder<NapBox> builder)
    {
        builder.ToTable("NapBoxes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Code).HasMaxLength(50).IsRequired();
        builder.Property(n => n.Area).HasMaxLength(100);
        builder.Property(n => n.Description).HasMaxLength(200);
        builder.Property(n => n.CreatedByUserId).HasMaxLength(450);
        builder.Property(n => n.UpdatedByUserId).HasMaxLength(450);
        builder.Property(n => n.ConcurrencyStamp).HasMaxLength(32).IsRequired().IsConcurrencyToken();
        builder.HasIndex(n => n.Code).IsUnique();
    }
}
