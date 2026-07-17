using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetWithDrHesham.Api.Data;
using OetWithDrHesham.Api.Domain;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Endpoints;

/// <summary>
/// Inbound webhook receiver for LiveKit (or compatible) provider events.
///
/// The route is intentionally public (no auth middleware) — instead, the
/// caller's <c>Authorization</c> header MUST contain a hex-encoded
/// HMAC-SHA256 of the raw request body, signed with
/// <c>LiveKit:WebhookSigningSecret</c>. Verification is delegated to
/// <see cref="ILiveKitGateway.VerifyWebhookSignature"/> so the stub and
/// the real implementation share a single contract.
///
/// Idempotency: providers retry on transient failures, so the endpoint
/// dedupes by the payload's <c>id</c> field via the shared
/// <see cref="IdempotencyRecord"/> entity (scope = <c>livekit_webhook</c>).
/// </summary>
public static class LiveKitWebhookEndpoint
{
    private const string IdempotencyScope = "livekit_webhook";

    public static IEndpointRouteBuilder MapLiveKitWebhookEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/webhooks/livekit", HandleAsync)
            .AllowAnonymous()
            .WithTags("Webhooks")
            .WithSummary("Inbound LiveKit provider webhook (HMAC-SHA256 signed).")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status400BadRequest);

        return app;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        ILiveKitGateway gateway,
        SpeakingLiveRoomService service,
        LearnerDbContext db,
        CancellationToken ct)
    {
        // Read the raw body verbatim — must match exactly what the
        // provider signed.
        http.Request.EnableBuffering();
        http.Request.Body.Position = 0;
        string payload;
        using (var reader = new StreamReader(http.Request.Body, leaveOpen: true))
        {
            payload = await reader.ReadToEndAsync(ct);
        }
        http.Request.Body.Position = 0;

        var signature = http.Request.Headers.TryGetValue("Authorization", out var headerValue)
            ? headerValue.ToString()
            : null;

        if (string.IsNullOrWhiteSpace(signature))
        {
            return Results.Unauthorized();
        }

        if (!gateway.VerifyWebhookSignature(payload, signature))
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return Results.BadRequest(new { errorCode = "empty_payload", message = "Webhook payload is empty." });
        }

        string eventType;
        string? webhookEventId;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            eventType = TryGetString(root, "event") ?? string.Empty;
            webhookEventId = TryGetString(root, "id");

            if (string.IsNullOrWhiteSpace(eventType))
            {
                return Results.BadRequest(new { errorCode = "event_required", message = "event field is required." });
            }
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { errorCode = "invalid_json", message = "Webhook payload is not valid JSON." });
        }

        // Idempotency dedupe: providers retry on transient errors, so a
        // unique (Scope, Key) on IdempotencyRecord prevents double-processing.
        if (!string.IsNullOrWhiteSpace(webhookEventId))
        {
            var existing = await db.IdempotencyRecords.AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.Scope == IdempotencyScope && r.Key == webhookEventId,
                    ct);
            if (existing is not null)
            {
                return Results.Accepted(value: new { status = "duplicate" });
            }

            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Id = $"livekit_{Guid.NewGuid():N}",
                Scope = IdempotencyScope,
                Key = webhookEventId,
                ResponseJson = "{}",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Concurrent dedupe write — treat as duplicate.
                return Results.Accepted(value: new { status = "duplicate" });
            }
        }

        await service.HandleWebhookAsync(eventType, payload, ct);

        return Results.Ok(new { status = "ok" });
    }

    private static string? TryGetString(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        if (!el.TryGetProperty(name, out var prop)) return null;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() : null;
    }
}
