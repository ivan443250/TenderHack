using TenderHack.Application.Tickets;
using TenderHack.Domain.Enums;

namespace TenderHack.Web.Endpoints;

public sealed class TicketEndpoints : IEndpointGroup
{
    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tickets").WithTags("Tickets");

        group.MapGet("/{ticketId:guid}", async (Guid ticketId, GetTicketHandler handler, CancellationToken ct) =>
        {
            var ticket = await handler.HandleAsync(new GetTicketQuery(ticketId), ct);
            return ticket is null ? Results.NotFound() : Results.Ok(ticket);
        });

        group.MapGet("/queue/{line}", async (SupportLine line, GetOperatorQueueHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new GetOperatorQueueQuery(line), ct)));

        group.MapPost("/{ticketId:guid}/take", async (Guid ticketId, TakeTicketRequest body, TakeTicketHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new TakeTicketCommand(ticketId, body.SpecialistId), ct);
            return result.IsSuccess ? Results.NoContent() : Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapPost("/{ticketId:guid}/messages", async (Guid ticketId, OperatorMessageRequest body, SendOperatorMessageHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new SendOperatorMessageCommand(ticketId, body.Text), ct);
            return result.IsSuccess ? Results.NoContent() : Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
        });

        group.MapPost("/{ticketId:guid}/resolve", async (Guid ticketId, ResolveTicketHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ResolveTicketCommand(ticketId), ct);
            return result.IsSuccess ? Results.NoContent() : Results.Problem(result.Error, statusCode: StatusCodes.Status400BadRequest);
        });
    }

    public sealed record TakeTicketRequest(Guid SpecialistId);

    public sealed record OperatorMessageRequest(string Text);
}
