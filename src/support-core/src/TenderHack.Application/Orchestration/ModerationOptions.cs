namespace TenderHack.Application.Orchestration;

/// <summary>Server configuration for the warning-first policy (architecture.md §5.4) — never a client input.</summary>
public sealed record ModerationOptions(int CloseAfterWarnings = 1);
