using TenderHack.Domain.Entities;
using TenderHack.Domain.Enums;

namespace TenderHack.Application.Abstractions;

public interface ITicketRepository
{
    Task<Ticket> GetOrCreateAsync(Guid userId, CancellationToken ct);

    Task<Ticket?> GetWithMessagesAsync(Guid ticketId, CancellationToken ct);

    Task<IReadOnlyList<Ticket>> GetQueueAsync(SupportLine line, CancellationToken ct);

    Task<Specialist?> GetSpecialistAsync(Guid specialistId, CancellationToken ct);

    void Add(Ticket ticket);
}
