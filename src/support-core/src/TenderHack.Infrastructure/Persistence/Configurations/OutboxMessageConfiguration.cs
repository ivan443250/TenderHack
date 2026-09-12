using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TenderHack.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessageEntity>
{
    public void Configure(EntityTypeBuilder<OutboxMessageEntity> builder)
    {
        builder.ToTable("outbox");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedOnAdd();
        builder.Property(m => m.MessageType).IsRequired();
        builder.Property(m => m.PayloadJson).IsRequired();
        builder.Property(m => m.CreatedAt).IsRequired();
        builder.HasIndex(m => m.DeliveredAt);
    }
}
