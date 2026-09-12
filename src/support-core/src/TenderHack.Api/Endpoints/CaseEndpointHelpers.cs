using TenderHack.Application.Exceptions;
using TenderHack.Domain.Cases;

namespace TenderHack.Api.Endpoints;

internal static class CaseEndpointHelpers
{
    public static string RequireOwner(HttpContext context) =>
        OwnerSession.TryGet(context) ?? throw new UnauthenticatedException();

    public static bool TryRequireCaseId(string raw, out CaseId id, out IResult badRequest)
    {
        if (CaseId.TryParse(raw, out id))
        {
            badRequest = Results.Empty;
            return true;
        }

        badRequest = Results.BadRequest(new { code = "VALIDATION_ERROR", message = "Invalid case id." });
        return false;
    }
}
