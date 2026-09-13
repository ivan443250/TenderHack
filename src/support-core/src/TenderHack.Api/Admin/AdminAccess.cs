using System.Security.Cryptography;
using System.Text;

namespace TenderHack.Api.Admin;

public enum AdminAccessResult
{
    Ok,
    Disabled,
    Unauthorized,
}

/// <summary>
/// The whole access policy for `/admin/*` in one testable function: disabled → 404 (the surface
/// does not exist), token configured but missing/wrong → 401, otherwise allowed. No owner cookie is
/// involved — the panel is cross-owner by design and must never be reachable through the SPA session.
/// </summary>
public static class AdminAccess
{
    public const string TokenHeader = "X-Admin-Token";
    public const string TokenQuery = "token";

    public static AdminAccessResult Check(AdminOptions options, string? presentedToken)
    {
        if (!options.Enabled)
        {
            return AdminAccessResult.Disabled;
        }

        if (string.IsNullOrEmpty(options.Token))
        {
            return AdminAccessResult.Ok;
        }

        if (string.IsNullOrEmpty(presentedToken))
        {
            return AdminAccessResult.Unauthorized;
        }

        var expected = Encoding.UTF8.GetBytes(options.Token);
        var actual = Encoding.UTF8.GetBytes(presentedToken);
        return CryptographicOperations.FixedTimeEquals(expected, actual) ? AdminAccessResult.Ok : AdminAccessResult.Unauthorized;
    }

    public static string? PresentedToken(HttpRequest request)
    {
        if (request.Headers.TryGetValue(TokenHeader, out var header) && !string.IsNullOrEmpty(header))
        {
            return header.ToString();
        }

        return request.Query.TryGetValue(TokenQuery, out var query) && !string.IsNullOrEmpty(query) ? query.ToString() : null;
    }
}
