using System.Text.Json;
using TenderHack.Domain.Handoffs;

namespace TenderHack.Infrastructure.Persistence;

/// <summary>
/// Serializes <see cref="HandoffPackage"/> to/from a single JSON column (`handoffs.package_json`) —
/// same reasoning as <see cref="TurnContextJson"/>: a small, whole-object read/write per handoff,
/// never queried by SQL.
/// </summary>
internal static class HandoffPackageJson
{
    public static string Serialize(HandoffPackage? package) => JsonSerializer.Serialize(package);

    public static HandoffPackage? Deserialize(string json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<HandoffPackage>(json);
}
