using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TenderHack.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<NotificationEntity>
{
    public void Configure(EntityTypeBuilder<NotificationEntity> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedOnAdd();
        builder.Property(n => n.OwnerId).IsRequired();
        builder.Property(n => n.CaseId).IsRequired();
        builder.Property(n => n.Type).IsRequired();
        builder.Property(n => n.OccurredAt).IsRequired();
        builder.Property(n => n.Title).IsRequired();
        builder.Property(n => n.Body).IsRequired();
        builder.HasIndex(n => new { n.OwnerId, n.Id });
    }
}
