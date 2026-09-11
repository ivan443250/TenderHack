using Microsoft.Extensions.Options;
using TenderHack.Application.Abstractions;
using TenderHack.Application.Analytics;

namespace TenderHack.Infrastructure.Services;

// Starts from appsettings ("Thresholds:MinConfidence") and can be changed at runtime through
// the admin endpoint (UpdateThresholdHandler) without a redeploy — see sections 8.1 and 9.4.
// The in-memory override is process-local by design; it resets to the configured value on restart.
public sealed class OptionsThresholdProvider(IOptions<ThresholdOptions> options)
    : IThresholdProvider, IThresholdStore
{
    private double? _override;

    public double MinConfidence => _override ?? options.Value.MinConfidence;

    public Task SetMinConfidenceAsync(double value, CancellationToken ct)
    {
        _override = value;
        return Task.CompletedTask;
    }
}
