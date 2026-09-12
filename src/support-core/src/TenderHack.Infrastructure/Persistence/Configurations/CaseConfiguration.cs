using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Handoffs;

namespace TenderHack.Infrastructure.Persistence.Configurations;

/// <summary>
/// Maps the `Case` aggregate (root + owned `Turn`s + owned `Handoff`) to `cases`/`turns`/`handoffs`
/// (architecture.md §8). Owned types are always loaded with their owner — no `.Include()` needed.
/// </summary>
public sealed class CaseConfiguration : IEntityTypeConfiguration<Case>
{
    public void Configure(EntityTypeBuilder<Case> builder)
    {
        builder.ToTable("cases");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasConversion(id => id.Value, value => new CaseId(value));

        builder.Property(c => c.OwnerId).IsRequired();
        builder.Property(c => c.ConversationStatus).HasConversion<string>().IsRequired();
        builder.Property(c => c.ResolutionStatus).HasConversion<string>().IsRequired();
        builder.Property(c => c.ModerationWarningCount).IsRequired();
        builder.Property(c => c.CompletionReason).HasConversion<string>();
        builder.Property(c => c.CompletedAt);

        builder.Ignore(c => c.ActiveTurn);

        builder.OwnsMany(c => c.Turns, turn =>
        {
            turn.ToTable("turns");
            turn.WithOwner().HasForeignKey("CaseId");
            turn.HasKey(t => t.Id);
            turn.Property(t => t.Id).HasConversion(id => id.Value, value => new TurnId(value));
            turn.Property(t => t.Revision).IsRequired();
            turn.Property(t => t.CreatedAt).IsRequired();
            turn.Property(t => t.Status).HasConversion<string>().IsRequired();
            turn.Property(t => t.Decision).HasConversion<string>();
        });

        builder.OwnsOne(c => c.Handoff, handoff =>
        {
            handoff.ToTable("handoffs");
            handoff.WithOwner().HasForeignKey(h => h.CaseId);
            handoff.HasKey(h => h.Id);
            handoff.Property(h => h.Id).HasConversion(id => id.Value, value => new HandoffId(value));
            handoff.Property(h => h.CaseId).HasConversion(id => id.Value, value => new CaseId(value));
            handoff.Property(h => h.Status).HasConversion<string>().IsRequired();
            handoff.Property(h => h.IntegrationMode).HasConversion<string>();
            handoff.Property(h => h.ExternalCaseId);
            handoff.Property(h => h.Terminal).HasConversion<string>();
            handoff.Property(h => h.LastExternalRevision).IsRequired();

            handoff.OwnsOne(h => h.Stage, stage =>
            {
                stage.Property(s => s.Code).HasColumnName("stage_code");
                stage.Property(s => s.DisplayName).HasColumnName("stage_display_name");
            });
            handoff.Navigation(h => h.Stage).IsRequired(false);

            handoff.OwnsOne(h => h.AssignedSpecialist, specialist =>
            {
                specialist.Property(s => s.Ref).HasColumnName("specialist_ref");
                specialist.Property(s => s.DisplayName).HasColumnName("specialist_display_name");
            });
            handoff.Navigation(h => h.AssignedSpecialist).IsRequired(false);
        });
        builder.Navigation(c => c.Handoff).IsRequired(false);
    }
}
