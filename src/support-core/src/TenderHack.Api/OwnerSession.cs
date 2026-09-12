namespace TenderHack.Api;

/// <summary>
/// Anonymous owner identity (web-api-v0.md §13): an opaque id in an `HttpOnly` cookie. No accounts,
/// no passwords — the cookie is the only credential the browser holds.
/// </summary>
public static class OwnerSession
{
    private const string CookieName = "owner_id";

    /// <summary>Reads the existing owner cookie, or null if the caller has none yet.</summary>
    public static string? TryGet(HttpContext context) =>
        context.Request.Cookies.TryGetValue(CookieName, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    /// <summary>Returns the existing owner cookie, or issues a fresh one (`POST /session`, first `POST /cases`).</summary>
    public static string GetOrCreate(HttpContext context)
    {
        if (TryGet(context) is { } existing)
        {
            return existing;
        }

        var ownerId = Guid.NewGuid().ToString("n");
        context.Response.Cookies.Append(CookieName, ownerId, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddYears(1),
        });
        return ownerId;
    }
}
