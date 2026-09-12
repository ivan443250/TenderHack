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
        builder.Property(n => n.SourceEventId).IsRequired();
        builder.HasIndex(n => new { n.OwnerId, n.Id });
        // architecture.md §7: notification unique by (owner_id, case_id, type, source_event_id) — a
        // redelivered case_events row (poll and webhook both observing the same fact, or an
        // at-least-once retry) can never produce a second inbox entry for it.
        builder.HasIndex(n => new { n.OwnerId, n.CaseId, n.Type, n.SourceEventId }).IsUnique();
    }
}
