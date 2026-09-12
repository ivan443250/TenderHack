using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using TenderHack.Api.Contracts;
using TenderHack.Application.Ports;
using TenderHack.Application.UseCases;
using TenderHack.Domain.Handoffs;
using TenderHack.Infrastructure.Handoff;

namespace TenderHack.Api.Endpoints;

/// <summary>
/// Inbound channel B (support-adapter-v0.md §6.2). Not for browsers — never reads the owner cookie.
/// Registered only when `Support:Webhook:Enabled=true`; the demo adapter never calls it.
/// </summary>
public static class SupportWebhookEndpoints
{
    private static readonly TimeSpan TimestampTolerance = TimeSpan.FromMinutes(5);

    public static void MapSupportWebhookEndpoints(this IEndpointRouteBuilder app, SupportOptions options)
    {
        if (!options.Webhook.Enabled)
        {
            return;
        }

        app.MapPost("/api/v0/integrations/support/status", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        ICaseRepository cases,
        IngestHandoffStatusUseCase ingest,
        IOptions<SupportOptions> options,
        IOptions<JsonOptions> jsonOptions,
        CancellationToken ct)
    {
        var secret = options.Value.Webhook.Secret;
        if (string.IsNullOrEmpty(secret))
        {
            return Results.Problem("Support:Webhook:Secret is not configured.", statusCode: StatusCodes.Status500InternalServerError);
        }

        using var bodyReader = new StreamReader(context.Request.Body);
        var rawBody = await bodyReader.ReadToEndAsync(ct);
        var rawBytes = Encoding.UTF8.GetBytes(rawBody);

        if (!IsValidTimestamp(context.Request.Headers["X-Support-Timestamp"]))
        {
            return Results.Unauthorized();
        }

        if (!IsValidSignature(context.Request.Headers["X-Support-Signature"], rawBytes, secret))
        {
            return Results.Unauthorized();
        }

        var snapshot = System.Text.Json.JsonSerializer.Deserialize<SupportStatusWebhookRequest>(rawBytes, jsonOptions.Value.SerializerOptions);
        if (snapshot is null || !HandoffId.TryParse(snapshot.HandoffId, out var handoffId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["handoff_id"] = ["Missing or invalid handoff_id."] });
        }

        var @case = await cases.FindByHandoffIdAsync(handoffId, ct);
        if (@case?.Handoff is not { } handoff)
        {
            return Results.NotFound(new { code = "NOT_FOUND", message = "Unknown handoff_id." });
        }

        if (snapshot.ExternalCaseId is { } externalCaseId
            && handoff.ExternalCaseId is { } persistedExternalCaseId
            && !string.Equals(externalCaseId, persistedExternalCaseId, StringComparison.Ordinal))
        {
            return Results.Conflict(new { code = "HANDOFF_MISMATCH", message = "handoff_id/external_case_id mismatch." });
        }

        var stage = snapshot.Stage is { } s ? new HandoffStage(s.Code, s.DisplayName) : null;
        var specialist = snapshot.AssignedSpecialist is { } a ? new AssignedSpecialist(a.Ref, a.DisplayName) : null;

        await ingest.ExecuteAsync(@case.Id, handoffId, snapshot.ExternalRevision, stage, specialist, snapshot.Terminal, ct);

        return Results.Accepted();
    }

    private static bool IsValidTimestamp(string? headerValue)
    {
        if (string.IsNullOrEmpty(headerValue))
        {
            return false;
        }

        DateTimeOffset timestamp;
        if (long.TryParse(headerValue, out var unixSeconds))
        {
            timestamp = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }
        else if (!DateTimeOffset.TryParse(headerValue, out timestamp))
        {
            return false;
        }

        return (DateTimeOffset.UtcNow - timestamp).Duration() <= TimestampTolerance;
    }

    private static bool IsValidSignature(string? headerValue, byte[] rawBody, string secret)
    {
        if (string.IsNullOrEmpty(headerValue) || !headerValue.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var providedHex = headerValue["sha256=".Length..];
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), rawBody);
        var expectedHex = Convert.ToHexStringLower(expected);

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(providedHex), Encoding.UTF8.GetBytes(expectedHex));
    }
}
