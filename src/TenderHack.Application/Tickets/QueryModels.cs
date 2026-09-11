using TenderHack.Domain.Enums;
using TenderHack.Domain.ValueObjects;

namespace TenderHack.Application.Tickets;

public sealed record TicketSummary(Guid Id, TicketStatus Status, SupportLine? AssignedLine, DateTimeOffset UpdatedAt);

public sealed record MessageView(Guid Id, MessageAuthor Author, string Text, DateTimeOffset At, IReadOnlyList<SourceRef> Sources);

public sealed record TicketView(Guid Id, TicketStatus Status, SupportLine? AssignedLine, IReadOnlyList<MessageView> Messages);
