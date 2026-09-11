using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderHack.Domain.Entities;

namespace TenderHack.Infrastructure.Persistence.Configurations;

public sealed class FeedbackConfiguration : IEntityTypeConfiguration<Feedback>
{
    public void Configure(EntityTypeBuilder<Feedback> builder)
    {
        builder.ToTable("feedback");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.IsPositive).IsRequired();
        builder.Property(f => f.At).IsRequired();
    }
}
