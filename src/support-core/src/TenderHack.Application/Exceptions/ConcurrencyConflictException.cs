namespace TenderHack.Application.Exceptions;

/// <summary>
/// The unit of work lost a race with a concurrent write to the same case (architecture.md §7):
/// an optimistic-concurrency check failed or a per-case uniqueness rule (one row per turn
/// revision) rejected the commit. Nothing was persisted; the caller reloads and retries.
/// </summary>
public sealed class ConcurrencyConflictException(string message, Exception? inner = null)
    : Exception(message, inner);
