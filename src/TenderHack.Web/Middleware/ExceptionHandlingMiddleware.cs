using TenderHack.Application.Common;
using TenderHack.Domain.Exceptions;

namespace TenderHack.Web.Middleware;

public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (TicketTransitionException ex)
        {
            logger.LogWarning(ex, "Invalid ticket transition");
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, ex.Message);
        }
        catch (ValidationException ex)
        {
            logger.LogWarning(ex, "Validation failed");
            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, ex.Message);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string detail)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new
        {
            type = $"https://httpstatuses.com/{statusCode}",
            title = statusCode == StatusCodes.Status409Conflict ? "Conflict" : "Bad Request",
            status = statusCode,
            detail
        });
    }
}
