using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderHack.Domain.Entities;

namespace TenderHack.Infrastructure.Persistence.Configurations;

public sealed class SpecialistConfiguration : IEntityTypeConfiguration<Specialist>
{
    public void Configure(EntityTypeBuilder<Specialist> builder)
    {
        builder.ToTable("specialists");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.FullName).IsRequired().HasMaxLength(256);
        builder.Property(s => s.Line).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.IsActive).IsRequired();
    }
}
