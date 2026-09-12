using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TenderHack.Api.Contracts;
using TenderHack.Application.Exceptions;
using TenderHack.Application.Ports;

namespace TenderHack.Api;

public static class IdempotencyScopes
{
    public const string CreateCase = "case.create";
    public const string SubmitFeedback = "case.feedback";
    public const string ConfirmHandoff = "handoff.confirm";
    public const string RetryHandoff = "handoff.retry";
    public const string SendMessage = "case.message";
    public const string CompleteCase = "case.complete";
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

    /// <summary>For endpoints (like `POST /messages`) where the thing worth replaying is the response itself, not a stable entity id.</summary>
    public static string SerializeCachedResponse<T>(T response) => JsonSerializer.Serialize(response);

    public static T DeserializeCachedResponse<T>(string cached) =>
        JsonSerializer.Deserialize<T>(cached) ?? throw new InvalidOperationException("Cached idempotent response could not be deserialized.");
}
