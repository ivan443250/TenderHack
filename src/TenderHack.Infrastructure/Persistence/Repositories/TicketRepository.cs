using Microsoft.EntityFrameworkCore;
using TenderHack.Application.Abstractions;
using TenderHack.Domain.Entities;
using TenderHack.Domain.Enums;

namespace TenderHack.Infrastructure.Persistence.Repositories;

public sealed class TicketRepository(AppDbContext db) : ITicketRepository
{
    public async Task<Ticket> GetOrCreateAsync(Guid userId, CancellationToken ct)
    {
        var openStatuses = new[]
        {
            TicketStatus.New, TicketStatus.BotAnswered, TicketStatus.AwaitingOperator, TicketStatus.InProgress
        };

        var existing = await db.Tickets
            .Include(t => t.Messages)
            .Where(t => t.UserId == userId && openStatuses.Contains(t.Status))
            .OrderByDescending(t => t.UpdatedAt)
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return existing;
        }

        var ticket = Ticket.Create(Guid.NewGuid(), userId, DateTimeOffset.UtcNow);
        db.Tickets.Add(ticket);
        return ticket;
    }

    public Task<Ticket?> GetWithMessagesAsync(Guid ticketId, CancellationToken ct) =>
        db.Tickets
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct);

    public async Task<IReadOnlyList<Ticket>> GetQueueAsync(SupportLine line, CancellationToken ct) =>
        await db.Tickets
            .AsNoTracking()
            .Where(t => t.AssignedLine == line && t.Status == TicketStatus.AwaitingOperator)
            .OrderBy(t => t.UpdatedAt)
            .ToListAsync(ct);

    public Task<Specialist?> GetSpecialistAsync(Guid specialistId, CancellationToken ct) =>
        db.Specialists.FirstOrDefaultAsync(s => s.Id == specialistId, ct);

    public void Add(Ticket ticket) => db.Tickets.Add(ticket);
}
