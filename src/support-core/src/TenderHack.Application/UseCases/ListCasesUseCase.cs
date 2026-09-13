using TenderHack.Application.Ports;
using TenderHack.Domain.Cases;

namespace TenderHack.Application.UseCases;

public sealed class ListCasesUseCase(ICaseRepository cases)
{
    public async Task<IReadOnlyList<Case>> ExecuteAsync(string ownerId, bool archivedOnly, CancellationToken ct)
    {
        var owned = await cases.ListByOwnerAsync(ownerId, ct);
        return
        [
            .. owned
                .Where(c => c.HiddenAt is null)
                .Where(c => archivedOnly ? c.ConversationStatus != ConversationStatus.Active : c.ConversationStatus == ConversationStatus.Active)
                .OrderByDescending(c => c.LastActivityAt),
        ];
    }
}
