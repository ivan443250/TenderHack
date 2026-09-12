namespace TenderHack.Application.Exceptions;

/// <summary>Same `Idempotency-Key` reused with a different payload (architecture.md §7).</summary>
public sealed class IdempotencyConflictException(string scope, string idempotencyKey)
    : InvalidOperationException($"Idempotency-Key '{idempotencyKey}' was already used for '{scope}' with a different payload.")
{
    public string Scope { get; } = scope;
    public string IdempotencyKey { get; } = idempotencyKey;
}
