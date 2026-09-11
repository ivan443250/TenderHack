using TenderHack.Application.Tickets;

namespace TenderHack.Web.Endpoints;

public sealed class FeedbackEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tickets/{ticketId:guid}/feedback").WithTags("Feedback");

        group.MapPost("/", async (Guid ticketId, SubmitFeedbackRequest body, SubmitFeedbackHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new SubmitFeedbackCommand(ticketId, body.IsPositive, body.Comment), ct);
            return result.IsSuccess ? Results.NoContent() : Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
        });
    }

    public sealed record SubmitFeedbackRequest(bool IsPositive, string? Comment);
}
