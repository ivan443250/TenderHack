namespace TenderHack.Application.Analytics;

public sealed record UpdateThresholdCommand(double MinConfidence);

// Backed by OptionsThresholdProvider (Infrastructure): writes the option so the value
// changes without redeploy — see sections 8.1 and 9.4.
public interface IThresholdStore
{
    Task SetMinConfidenceAsync(double value, CancellationToken ct);
}

public sealed class UpdateThresholdHandler(IThresholdStore store)
{
    public Task HandleAsync(UpdateThresholdCommand command, CancellationToken ct) =>
        store.SetMinConfidenceAsync(command.MinConfidence, ct);
}
