using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TenderHack.Infrastructure.Persistence.Configurations;

public sealed class CaseEventConfiguration : IEntityTypeConfiguration<CaseEventEntity>
{
    public void Configure(EntityTypeBuilder<CaseEventEntity> builder)
    {
        builder.ToTable("case_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedOnAdd();
        builder.Property(e => e.CaseId).IsRequired();
        builder.Property(e => e.Type).IsRequired();
        builder.Property(e => e.OccurredAt).IsRequired();
        builder.Property(e => e.PayloadJson).IsRequired();
        builder.HasIndex(e => new { e.CaseId, e.Id });
    }
}
