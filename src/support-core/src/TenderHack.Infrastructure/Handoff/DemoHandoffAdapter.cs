using Microsoft.Extensions.Options;
using TenderHack.Application.Ports;
using TenderHack.Domain.Handoffs;

namespace TenderHack.Infrastructure.Handoff;

/// <summary>
/// P0 adapter, controlled by team config and explicitly demo-labelled (architecture.md §15,
/// support-adapter-v0.md §8). Every fact it returns carries `IntegrationMode.Simulated`.
/// </summary>
public sealed class DemoHandoffAdapter(IOptions<SupportOptions> options, TimeProvider clock) : IHandoffAdapter
{
    public Task<HandoffAck> SubmitAsync(HandoffRequest request, string idempotencyKey, CancellationToken ct)
    {
        var demo = options.Value.Demo;
        return demo.Submit switch
        {
            DemoSubmitMode.Success => Task.FromResult(new HandoffAck(true, true, DeriveExternalCaseId(request.HandoffId))),
            DemoSubmitMode.Failure => Task.FromResult(new HandoffAck(false, true, null, "Демо-адаптер отклонил обращение.")),
            DemoSubmitMode.Timeout => Task.FromException<HandoffAck>(new TimeoutException("Demo adapter simulated a submit timeout.")),
            _ => throw new InvalidOperationException($"Unknown demo submit mode: {demo.Submit}"),
        };
    }

    public Task<HandoffStatusSnapshot?> GetStatusAsync(HandoffStatusQuery query, CancellationToken ct)
    {
        var demo = options.Value.Demo;
        var externalCaseId = query.ExternalCaseId ?? DeriveExternalCaseId(query.HandoffId);

        if (demo.Status == DemoStatusMode.None)
        {
            // "returns the acceptance snapshot forever (no stage, no specialist)" — nothing new to
            // report beyond the initial acceptance, so there is no fresh revision to apply.
            return Task.FromResult<HandoffStatusSnapshot?>(null);
        }

        // One step per poll, in script order — never "whatever is latest by now", or a poll interval
        // longer than the stage delay would skip the ASSIGNED step and the specialist with it.
        var elapsed = clock.GetUtcNow() - query.AcceptedAt;
        var step = DemoHandoffScript.NextStep(elapsed, TimeSpan.FromSeconds(demo.StageDelaySeconds), query.LastKnownExternalRevision ?? 0);
        if (step is null)
        {
            return Task.FromResult<HandoffStatusSnapshot?>(null);
        }

        return Task.FromResult<HandoffStatusSnapshot?>(new HandoffStatusSnapshot(
            externalCaseId, step.Stage, step.AssignedSpecialist, step.Terminal, step.ExternalRevision,
            clock.GetUtcNow(), IntegrationMode.Simulated));
    }

    private static string DeriveExternalCaseId(HandoffId handoffId) => $"demo-{handoffId}";
}
