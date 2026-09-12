using TenderHack.Domain.Cases;
using TenderHack.Domain.Feedback;

namespace TenderHack.Application.Ports;

public interface IFeedbackRepository
{
    void Add(Feedback feedback);

    Task<Feedback?> FindByCaseIdAsync(CaseId caseId, CancellationToken ct);
}
