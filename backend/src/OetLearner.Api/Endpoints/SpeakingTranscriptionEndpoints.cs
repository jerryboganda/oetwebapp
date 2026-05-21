using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OetLearner.Api.Data;
using OetLearner.Api.Domain;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// HTTP surface for the Speaking ASR transcription pipeline (Phase 4
/// of the OET Speaking module plan).
///
/// Two routes are exposed:
/// <list type="bullet">
///   <item><c>POST /v1/admin/speaking/transcribe/{sessionId}</c> — admin
///         trigger to (re-)enqueue a transcription. Body optionally
///         carries the <c>recordingMediaAssetId</c>; if omitted, the
///         most recent recording for the session is used.</item>
///   <item><c>GET /v1/speaking/sessions/{sessionId}/transcript</c> —
///         learner read of their own session's current transcript +
///         pipeline status.</item>
/// </list>
///
/// Wire-up: <c>Program.cs</c> (owned by Agent W2-A) calls
/// <see cref="MapSpeakingTranscriptionEndpoints"/>.
/// </summary>
public static class SpeakingTranscriptionEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingTranscriptionEndpoints(this WebApplication app)
    {
        // ── Admin trigger ──
        app.MapPost("/v1/admin/speaking/transcribe/{sessionId}", AdminTranscribeAsync)
            .RequireAuthorization("AdminOnly")
            .WithTags("Speaking transcription")
            .WithSummary("Admin: queue (or re-queue) an ASR transcription for a Speaking session.")
            .Produces<SpeakingTranscriptionStatus>(StatusCodes.Status202Accepted)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest);

        // ── Learner read ──
        app.MapGet("/v1/speaking/sessions/{sessionId}/transcript", LearnerGetTranscriptAsync)
            .RequireAuthorization("LearnerOnly")
            .WithTags("Speaking transcription")
            .WithSummary("Learner: read the latest ASR transcript + pipeline status for the caller's own session.")
            .Produces<SpeakingTranscriptResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    // ─────────────────────────────────────────────────────────────────
    // Admin: POST /v1/admin/speaking/transcribe/{sessionId}
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> AdminTranscribeAsync(
        string sessionId,
        HttpContext http,
        [FromBody] AdminSpeakingTranscribeRequest? request,
        SpeakingTranscriptionPipeline pipeline,
        LearnerDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Results.BadRequest(new { errorCode = "session_id_required", message = "sessionId is required." });
        }

        var session = await db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            return Results.NotFound(new { errorCode = "session_not_found", message = $"Speaking session '{sessionId}' does not exist." });
        }

        var recordingId = request?.RecordingMediaAssetId;
        if (string.IsNullOrWhiteSpace(recordingId))
        {
            // Fall back to the most recent recording on the session.
            var latestRecording = await db.SpeakingRecordings
                .AsNoTracking()
                .Where(r => r.SpeakingSessionId == sessionId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new { r.Id, r.MediaAssetId })
                .FirstOrDefaultAsync(ct);
            if (latestRecording is null)
            {
                return Results.BadRequest(new
                {
                    errorCode = "no_recording",
                    message = $"Speaking session '{sessionId}' has no recordings; nothing to transcribe.",
                });
            }
            recordingId = latestRecording.MediaAssetId;
        }

        try
        {
            await pipeline.EnqueueAsync(sessionId, recordingId!, ct);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { errorCode = "enqueue_failed", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { errorCode = "invalid_argument", message = ex.Message });
        }

        var status = await pipeline.GetStatusAsync(sessionId, ct);
        return Results.Accepted($"/v1/speaking/sessions/{sessionId}/transcript", status);
    }

    // ─────────────────────────────────────────────────────────────────
    // Learner: GET /v1/speaking/sessions/{sessionId}/transcript
    // ─────────────────────────────────────────────────────────────────
    private static async Task<IResult> LearnerGetTranscriptAsync(
        string sessionId,
        HttpContext http,
        SpeakingTranscriptionPipeline pipeline,
        LearnerDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return Results.BadRequest(new { errorCode = "session_id_required", message = "sessionId is required." });
        }

        var userId = http.UserId();

        var session = await db.SpeakingSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
        {
            return Results.NotFound(new
            {
                errorCode = "session_not_found",
                message = $"Speaking session '{sessionId}' does not exist.",
            });
        }
        if (!string.Equals(session.UserId, userId, StringComparison.Ordinal))
        {
            // IDOR guard — only the session owner can read its transcript.
            // Admins/experts read via a different endpoint (TODO Wave 5).
            return Results.Forbid();
        }

        var status = await pipeline.GetStatusAsync(sessionId, ct);

        // Fetch the latest completed transcript row (if any) so the
        // learner gets the actual segments alongside the status, in one
        // round trip.
        SpeakingTranscript? latest = null;
        if (!string.IsNullOrWhiteSpace(status.LatestTranscriptId))
        {
            latest = await db.SpeakingTranscripts
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == status.LatestTranscriptId, ct);
        }

        var response = new SpeakingTranscriptResponse(
            SpeakingSessionId: sessionId,
            Status: status,
            Transcript: latest is null ? null : new SpeakingTranscriptPayload(
                Id: latest.Id,
                Provider: latest.Provider,
                Language: latest.Language,
                Segments: SafeParseSegments(latest.SegmentsJson),
                WordCount: latest.WordCount,
                MeanConfidence: latest.MeanConfidence,
                GeneratedAt: latest.GeneratedAt));

        return Results.Ok(response);
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private static JsonElement SafeParseSegments(string segmentsJson)
    {
        if (string.IsNullOrWhiteSpace(segmentsJson))
        {
            using var empty = JsonDocument.Parse("[]");
            return empty.RootElement.Clone();
        }
        try
        {
            using var doc = JsonDocument.Parse(segmentsJson);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var empty = JsonDocument.Parse("[]");
            return empty.RootElement.Clone();
        }
    }

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}

// ─────────────────────────────────────────────────────────────────────
// DTOs (kept colocated with the endpoint — only ever used here).
// ─────────────────────────────────────────────────────────────────────

public sealed record AdminSpeakingTranscribeRequest(string? RecordingMediaAssetId);

public sealed record SpeakingTranscriptResponse(
    string SpeakingSessionId,
    SpeakingTranscriptionStatus Status,
    SpeakingTranscriptPayload? Transcript);

public sealed record SpeakingTranscriptPayload(
    string Id,
    string Provider,
    string Language,
    JsonElement Segments,
    int WordCount,
    double MeanConfidence,
    DateTimeOffset GeneratedAt);
