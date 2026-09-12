using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TenderHack.Application.Exceptions;
using TenderHack.Application.Ports;

namespace TenderHack.Api;

public static class IdempotencyScopes
{
    public const string CreateCase = "case.create";
    public const string SubmitFeedback = "case.feedback";
    public const string ConfirmHandoff = "handoff.confirm";
    public const string RetryHandoff = "handoff.retry";
}

public static class IdempotencyHelper
{
    public static string? GetKey(HttpContext context) =>
        context.Request.Headers["Idempotency-Key"] is { Count: > 0 } values ? values[0] : null;

    public static string HashPayload(object payload) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));

    /// <summary>
    /// No key on the request → null, proceed normally (architecture.md §7: the header is accepted,
    /// never required). A previously-seen key with a matching payload hash → the stored entity id to
    /// replay. A previously-seen key with a different hash → throws <see cref="IdempotencyConflictException"/>.
    /// </summary>
    public static async Task<string?> CheckAsync(
        IIdempotencyStore store, string ownerId, string scope, string? idempotencyKey, object payload, CancellationToken ct)
    {
        if (idempotencyKey is null)
        {
            return null;
        }

        var existing = await store.FindAsync(ownerId, scope, idempotencyKey, ct);
        if (existing is null)
        {
            return null;
        }

        if (existing.PayloadHash != HashPayload(payload))
        {
            throw new IdempotencyConflictException(scope, idempotencyKey);
        }

        return existing.EntityId;
    }
}
