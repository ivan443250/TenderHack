using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;
using TenderHack.Domain.Feedback;

namespace TenderHack.Application.Tests.Fakes;

public sealed class FakeFeedbackRepository : IFeedbackRepository
{
    public List<Feedback> Added { get; } = [];

    public void Add(Feedback feedback) => Added.Add(feedback);

    public Task<Feedback?> FindByCaseIdAsync(CaseId caseId, CancellationToken ct) =>
        Task.FromResult(Added.FirstOrDefault(f => f.CaseId == caseId));
}
