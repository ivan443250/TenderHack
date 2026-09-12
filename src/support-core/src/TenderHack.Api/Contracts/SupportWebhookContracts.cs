using TenderHack.Domain.Handoffs;

namespace TenderHack.Api.Contracts;

/// <summary>support-adapter-v0.md §6.2 — the raw `HandoffStatusSnapshot` JSON a real adapter pushes.</summary>
public sealed record SupportStatusWebhookRequest(
    string HandoffId,
    string? ExternalCaseId,
    IntegrationMode IntegrationMode,
    long ExternalRevision,
    DateTimeOffset OccurredAt,
    WebhookStageDto? Stage,
    WebhookSpecialistDto? AssignedSpecialist,
    HandoffTerminalOutcome? Terminal,
    string? ProviderCode,
    string? SafeMessage);

public sealed record WebhookStageDto(string Code, string? DisplayName);

public sealed record WebhookSpecialistDto(string Ref, string? DisplayName);
