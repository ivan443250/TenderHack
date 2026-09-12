using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TenderHack.Application.Exceptions;
using TenderHack.Application.Knowledge;
using TenderHack.Domain;

namespace TenderHack.Api.ExceptionHandling;

/// <summary>
/// Maps known exceptions to a stable machine `code` + safe message (web-api-v0.md §10). Anything
/// unmapped becomes a plain 500 — never a fabricated "no information" answer.
/// </summary>
public sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, detail) = exception switch
        {
            UnauthenticatedException => (StatusCodes.Status401Unauthorized, "UNAUTHENTICATED", "No owner session — call POST /api/v0/session first."),
            CaseNotFoundException => (StatusCodes.Status404NotFound, "NOT_FOUND", "Case not found."),
            CaseClosedException => (StatusCodes.Status409Conflict, "CASE_CLOSED", "This case is closed."),
            CaseAlreadyCompletedException => (StatusCodes.Status409Conflict, "CASE_CLOSED", "This case is already completed."),
            KnowledgeFailureException { Category: KnowledgeFailureCategory.Timeout } => (StatusCodes.Status504GatewayTimeout, "KNOWLEDGE_TIMEOUT", "Knowledge service timed out."),
            KnowledgeFailureException => (StatusCodes.Status503ServiceUnavailable, "KNOWLEDGE_UNAVAILABLE", "Knowledge service is unavailable."),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred."),
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = detail,
            Extensions = { ["code"] = code },
        }, cancellationToken);

        return true;
    }
}
