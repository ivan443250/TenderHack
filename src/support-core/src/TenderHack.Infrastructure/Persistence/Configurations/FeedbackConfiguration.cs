using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Feedback;

namespace TenderHack.Infrastructure.Persistence.Configurations;

public sealed class FeedbackConfiguration : IEntityTypeConfiguration<Feedback>
{
    public void Configure(EntityTypeBuilder<Feedback> builder)
    {
        builder.ToTable("feedback");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasConversion(id => id.Value, value => new FeedbackId(value));
        builder.Property(f => f.CaseId).HasConversion(id => id.Value, value => new CaseId(value)).IsRequired();
        builder.Property(f => f.SpecialistRating).HasConversion<string>();
        builder.Property(f => f.InformationQualityRating).HasConversion<string>();
        builder.Property(f => f.Solved);
        builder.Property(f => f.CommentText);
        builder.Property(f => f.SubmittedAt).IsRequired();

        builder.HasIndex(f => f.CaseId).IsUnique();
    }
}
