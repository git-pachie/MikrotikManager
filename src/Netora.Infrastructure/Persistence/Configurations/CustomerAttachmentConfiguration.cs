using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Netora.Domain.Customers;

namespace Netora.Infrastructure.Persistence.Configurations;

internal sealed class CustomerAttachmentConfiguration : IEntityTypeConfiguration<CustomerAttachment>
{
    public void Configure(EntityTypeBuilder<CustomerAttachment> builder)
    {
        builder.ToTable("CustomerAttachments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.FileName).HasMaxLength(200).IsRequired();
        builder.Property(a => a.StoredFileName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.ContentType).HasMaxLength(50).IsRequired();
        builder.Property(a => a.UploadedByUserId).HasMaxLength(450).IsRequired();
        builder.Property(a => a.UploadedByUserName).HasMaxLength(256).IsRequired();
        builder.Property(a => a.DeletedByUserId).HasMaxLength(450);
        builder.Property(a => a.DeletedByUserName).HasMaxLength(256);

        // Attachments are retained with the customer; a customer row is never deleted, so no cascade.
        builder.HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(a => new { a.CustomerId, a.IsDeleted });
        builder.HasIndex(a => a.StoredFileName).IsUnique();
    }
}
