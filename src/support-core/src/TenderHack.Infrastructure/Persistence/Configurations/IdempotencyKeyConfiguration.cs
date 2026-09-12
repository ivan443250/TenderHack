using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TenderHack.Infrastructure.Persistence.Configurations;

public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKeyEntity>
{
    public void Configure(EntityTypeBuilder<IdempotencyKeyEntity> builder)
    {
        builder.ToTable("idempotency_keys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedOnAdd();
        builder.Property(k => k.OwnerId).IsRequired();
        builder.Property(k => k.Scope).IsRequired();
        builder.Property(k => k.Key).IsRequired();
        builder.Property(k => k.PayloadHash).IsRequired();
        builder.Property(k => k.EntityId).IsRequired();
        builder.Property(k => k.CreatedAt).IsRequired();
        builder.HasIndex(k => new { k.OwnerId, k.Scope, k.Key }).IsUnique();
    }
}
