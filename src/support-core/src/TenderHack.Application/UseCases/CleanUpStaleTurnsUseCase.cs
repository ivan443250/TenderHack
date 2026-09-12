using TenderHack.Application.Ports;

namespace TenderHack.Application.UseCases;

/// <summary>
/// `stale-turn cleanup` (architecture.md §12): a turn stuck `QUEUED`/`RUNNING` past <paramref name="ttl"/>
/// almost always means the process crashed mid-turn. Failing it lets the case recover — a new user
/// message still works either way (`StartTurn` always supersedes), but without this a user who never
/// sends another message is stuck looking at a turn that never finishes.
/// </summary>
public sealed class CleanUpStaleTurnsUseCase(ICaseRepository cases, IUnitOfWork unitOfWork, ITurnEventStream events, TimeProvider clock)
{
    public async Task<int> ExecuteAsync(TimeSpan ttl, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var stale = await cases.ListStaleActiveTurnCasesAsync(now - ttl, ct);

        foreach (var @case in stale)
        {
            var turn = @case.ActiveTurn!;
            if (!@case.TryFailTurn(turn.Id, turn.Revision))
            {
                continue;
            }

            await events.PublishAsync(@case.Id, new CaseEvent("TECHNICAL_ERROR", turn.Id, turn.Revision, now,
                new Dictionary<string, object?> { ["category"] = "STALE_TURN" }), ct);
        }

        if (stale.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return stale.Count;
    }
}
