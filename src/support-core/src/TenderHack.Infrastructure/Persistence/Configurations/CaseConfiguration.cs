using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Feedback;
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

        // A message-send turn completing and a handoff-status webhook arriving for the same case at
        // the same moment must not silently lost-update each other (architecture.md §7 concurrency).
        // Postgres's own `xmin` system column is a free optimistic-concurrency token — no extra
        // migration column needed.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasConversion(id => id.Value, value => new CaseId(value));

        builder.Property(c => c.OwnerId).IsRequired();
        builder.Property(c => c.CreatedAt).IsRequired();
        builder.Property(c => c.ConversationStatus).HasConversion<string>().IsRequired();
        builder.Property(c => c.ResolutionStatus).HasConversion<string>().IsRequired();
        builder.Property(c => c.ModerationWarningCount).IsRequired();
        builder.Property(c => c.CompletionReason).HasConversion<string>();
        builder.Property(c => c.CompletedAt);
        builder.Property(c => c.HiddenAt).HasColumnName("hidden_at");

        // TurnContext (architecture.md §10 continuity) is stored as one JSON column rather than an
        // owned collection: it is always read/written whole per turn, never queried by SQL, and its
        // KnownSlots have no identity of their own to key an owned table on. A record's default
        // Equals/GetHashCode compares each property by reference for its list-typed members, so a
        // freshly-rebuilt "same content" instance would otherwise never equal the tracked original —
        // an explicit ValueComparer based on the same JSON round-trip is what makes EF Core actually
        // detect and persist a mutation (`Case` always replaces its own reference wholesale, never
        // mutates one in place, precisely so this comparison is meaningful).
        var turnContextComparer = new ValueComparer<TurnContext>(
            (left, right) => TurnContextJson.Serialize(left!) == TurnContextJson.Serialize(right!),
            value => TurnContextJson.Serialize(value).GetHashCode(),
            value => TurnContextJson.Deserialize(TurnContextJson.Serialize(value)));
        builder.Property(c => c.TurnContext)
            .HasColumnName("turn_context_json")
            .HasConversion(
                (TurnContext value) => TurnContextJson.Serialize(value),
                (string json) => TurnContextJson.Deserialize(json),
                turnContextComparer)
            .IsRequired();

        builder.Property(c => c.FeedbackId).HasConversion(
            id => id == null ? (Guid?)null : id.Value.Value,
            value => value == null ? (FeedbackId?)null : new FeedbackId(value.Value));

        builder.Ignore(c => c.ActiveTurn);
        builder.Ignore(c => c.LastActivityAt);

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

            // Its own `xmin` — separate from `cases`' own token above. A long-running turn (real
            // knowledge/model latency) publishing its final decision only ever UPDATEs this `turns`
            // row, never the `cases` row itself, so the `cases`-level token cannot catch a stale
            // publish. Without this, a superseded turn's own `UPDATE ... SET status='Completed'`
            // would silently succeed and overwrite the newer request's `Superseded` write — exactly
            // the invariant "running old turn cannot publish after a newer revision supersedes it"
            // (architecture.md §6) requires the DB, not just in-process state, to enforce.
            turn.Property<uint>("xmin").IsRowVersion();

            // Two requests that loaded the same case snapshot compute the same next revision; the
            // `xmin` token above cannot catch that (starting a turn only inserts here, it never
            // updates `cases`), so uniqueness is what turns the loser into a 409 instead of a
            // second "revision N" row (architecture.md §7: turn publication unique by turn + revision).
            turn.HasIndex("CaseId", nameof(Turn.Revision)).IsUnique();
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
            handoff.Property(h => h.AcceptedAt);
            handoff.Property(h => h.Stale).IsRequired();
            handoff.Property(h => h.ConfirmedSummary);

            // Same JSON-column + explicit ValueComparer pattern as `Case.TurnContext`
            // (CaseConfiguration above) and for the same reason — required.
            var packageComparer = new ValueComparer<HandoffPackage?>(
                (left, right) => HandoffPackageJson.Serialize(left) == HandoffPackageJson.Serialize(right),
                value => HandoffPackageJson.Serialize(value).GetHashCode(),
                value => HandoffPackageJson.Deserialize(HandoffPackageJson.Serialize(value)));
            handoff.Property(h => h.Package)
                .HasColumnName("package_json")
                .HasConversion(
                    (HandoffPackage? value) => HandoffPackageJson.Serialize(value),
                    (string json) => HandoffPackageJson.Deserialize(json),
                    packageComparer)
                .IsRequired();

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
