using Microsoft.EntityFrameworkCore;
using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Feedback;

namespace TenderHack.Infrastructure.Persistence;

public sealed class FeedbackRepository(TenderHackDbContext db) : IFeedbackRepository
{
    public void Add(Feedback feedback) => db.Feedbacks.Add(feedback);

    public Task<Feedback?> FindByCaseIdAsync(CaseId caseId, CancellationToken ct) =>
        db.Feedbacks.FirstOrDefaultAsync(f => f.CaseId == caseId, ct);
}
