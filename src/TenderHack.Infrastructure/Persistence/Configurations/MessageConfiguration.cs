using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderHack.Domain.Entities;
using TenderHack.Domain.ValueObjects;

namespace TenderHack.Infrastructure.Persistence.Configurations;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Author).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.Text).IsRequired();
        builder.Property(m => m.At).IsRequired();

        builder.Property(m => m.Sources)
            .HasColumnName("sources")
            .HasColumnType("jsonb")
            .HasConversion(
                sources => JsonSerializer.Serialize(sources, JsonSerializerOptions.Web),
                json => JsonSerializer.Deserialize<IReadOnlyList<SourceRef>>(json, JsonSerializerOptions.Web) ?? new List<SourceRef>());

        builder.OwnsOne(m => m.Analysis, analysis =>
        {
            analysis.ToTable("message_analysis");
            analysis.WithOwner().HasForeignKey("MessageId");
            analysis.Property(a => a.Line).HasConversion<string?>().HasMaxLength(16);
        });

        builder.Navigation(m => m.Analysis).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
