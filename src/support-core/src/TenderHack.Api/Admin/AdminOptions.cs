namespace TenderHack.Api.Admin;

/// <summary>
/// Test/demo admin panel (`/admin`) settings. The panel is a team-only diagnostic surface outside
/// `web-api-v0` — it is not linked from the SPA and carries no product state of its own. There are
/// no accounts/roles in P0 (hackathon-requirements.md §3), so access is gated only by this flag and
/// an optional shared token; keep it disabled or tokened on any host that is not a local demo box.
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public bool Enabled { get; set; }

    /// <summary>Optional shared secret; when non-empty every request must carry it as `X-Admin-Token` or `?token=`.</summary>
    public string? Token { get; set; }
}
