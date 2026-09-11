using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderHack.Domain.Entities;

namespace TenderHack.Infrastructure.Persistence.Configurations;

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("tickets");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.UserId).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.AssignedLine).HasConversion<string?>().HasMaxLength(16);
        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.UpdatedAt).IsRequired();

        builder.HasMany(t => t.Messages)
            .WithOne()
            .HasForeignKey(m => m.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Messages)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne(t => t.Feedback)
            .WithOne()
            .HasForeignKey<Feedback>(f => f.TicketId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(t => t.Feedback)
            .AutoInclude()
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
