using System.Text.Json;
using TenderHack.Domain.Cases;

namespace TenderHack.Infrastructure.Persistence;

/// <summary>
/// Serializes <see cref="TurnContext"/> to/from a single JSON column (`cases.turn_context_json`)
/// rather than an EF owned-collection model: `TurnContext` is a small, whole-object read/write
/// per turn, never queried by SQL, and this avoids inventing synthetic identity for its
/// <see cref="ContextSlot"/> list (architecture.md §10: avoid ceremonial layers that don't protect
/// a real invariant).
/// </summary>
internal static class TurnContextJson
{
    public static string Serialize(TurnContext context) => JsonSerializer.Serialize(context);

    public static TurnContext Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            // A row from before this column existed, or the migration's own column default.
            return TurnContext.Empty;
        }

        return JsonSerializer.Deserialize<TurnContext>(json) ?? TurnContext.Empty;
    }
}
