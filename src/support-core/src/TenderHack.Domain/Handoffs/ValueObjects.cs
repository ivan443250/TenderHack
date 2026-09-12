namespace TenderHack.Domain.Handoffs;

/// <summary>Adapter fact, not a Domain enum (architecture.md §6) — the code is opaque to Domain policy.</summary>
public sealed record HandoffStage(string Code, string? DisplayName);

/// <summary>Adapter fact; <see cref="Ref"/> is an opaque identifier, never a Domain concept of "employee".</summary>
public sealed record AssignedSpecialist(string Ref, string? DisplayName);
