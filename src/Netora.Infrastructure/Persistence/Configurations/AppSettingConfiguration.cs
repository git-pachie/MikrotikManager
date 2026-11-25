using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Netora.Domain.Settings;

namespace Netora.Infrastructure.Persistence.Configurations;

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(100);
        // Encrypted values are much longer than the plain text.
        builder.Property(s => s.Value).HasMaxLength(2000);
        builder.Property(s => s.UpdatedByUserId).HasMaxLength(450);
    }
}
