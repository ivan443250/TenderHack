using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TenderHack.Application.Exceptions;
using TenderHack.Application.Knowledge;
using TenderHack.Domain;
using TenderHack.Domain.Handoffs;

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
            FeedbackAlreadySubmittedException => (StatusCodes.Status409Conflict, "FEEDBACK_ALREADY_SUBMITTED", "Feedback was already submitted for this case."),
            CaseNotCompletedException => (StatusCodes.Status409Conflict, "CASE_NOT_COMPLETED", "This case is not completed yet."),
            InvalidHandoffTransitionException => (StatusCodes.Status409Conflict, "HANDOFF_INVALID_STATE", "The handoff is not in a state that allows this action."),
            HandoffAlreadyExistsException => (StatusCodes.Status409Conflict, "HANDOFF_ALREADY_EXISTS", "This case already has a handoff."),
            HandoffNotFoundException => (StatusCodes.Status404NotFound, "HANDOFF_NOT_FOUND", "This case has no handoff yet."),
            IdempotencyConflictException => (StatusCodes.Status409Conflict, "IDEMPOTENCY_KEY_CONFLICT", "Idempotency-Key was already used with a different request body."),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT", "This case was updated concurrently — reload and retry."),
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
