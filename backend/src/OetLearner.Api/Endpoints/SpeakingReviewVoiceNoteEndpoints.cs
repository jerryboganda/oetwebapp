using System.Security.Claims;
using OetLearner.Api.Services.Speaking;

namespace OetLearner.Api.Endpoints;

// P4.4 — Speaking expert review voice-note endpoints.
//
// Sister surface of the writing voice-note routes on ExpertEndpoints
// (`/v1/expert/reviews/{reviewRequestId}/writing/voice-notes`). Kept in
// its own endpoint file so the speaking flow can grow independently
// (e.g. ASR-driven transcript backfill, per-criterion drill-down, etc.).
//
// All routes require the existing `ExpertOnly` policy. Admins are
// allowed to delete (we forward an `isAdmin` flag to the service).
public static class SpeakingReviewVoiceNoteEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingReviewVoiceNoteEndpoints(this IEndpointRouteBuilder app)
    {
        var expert = app.MapGroup("/v1/expert/speaking")
            .RequireAuthorization("ExpertOnly")
            .RequireRateLimiting("PerUser")
            .WithTags("Speaking expert voice notes");

        expert.MapPost("/reviews/{reviewRequestId}/voice-notes", async (
                string reviewRequestId,
                HttpContext http,
                SpeakingReviewVoiceNoteCreateRequest body,
                SpeakingReviewVoiceNoteService service,
                CancellationToken ct) =>
            {
                var expertId = http.ExpertId();
                var note = await service.CreateAsync(
                    reviewRequestId,
                    expertId,
                    body.MediaAssetId,
                    body.DurationSeconds,
                    body.WrittenNotes,
                    body.RubricJson,
                    ct);
                return Results.Ok(Project(note));
            })
            .RequireRateLimiting("PerUserWrite")
            .WithSummary("Attach a recorded voice-note (with optional notes / rubric) to a speaking expert review.");

        expert.MapGet("/reviews/{reviewRequestId}/voice-notes", async (
                string reviewRequestId,
                SpeakingReviewVoiceNoteService service,
                CancellationToken ct) =>
            {
                var notes = await service.ListForReviewAsync(reviewRequestId, ct);
                return Results.Ok(new
                {
                    reviewRequestId,
                    items = notes.Select(Project).ToList(),
                });
            })
            .WithSummary("List voice notes recorded against a speaking expert review, newest first.");

        expert.MapDelete("/voice-notes/{voiceNoteId}", async (
                string voiceNoteId,
                HttpContext http,
                SpeakingReviewVoiceNoteService service,
                CancellationToken ct) =>
            {
                var expertId = http.ExpertId();
                var isAdmin = http.User.IsInRole("admin") || http.User.IsInRole("Admin");
                await service.DeleteAsync(voiceNoteId, expertId, ct, isAdmin);
                return Results.NoContent();
            })
            .RequireRateLimiting("PerUserWrite")
            .WithSummary("Delete a speaking review voice note — author or admin only.");

        return app;
    }

    private static object Project(Domain.SpeakingReviewVoiceNote note) => new
    {
        note.Id,
        note.ReviewRequestId,
        note.ExpertUserId,
        note.MediaAssetId,
        note.DurationSeconds,
        note.TranscriptText,
        note.WrittenNotes,
        note.RubricJson,
        note.CreatedAt,
    };

    private static string ExpertId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated expert id is required.");
}

/// <summary>Request body for creating a new speaking review voice note.</summary>
/// <param name="MediaAssetId">Id of the uploaded audio MediaAsset to attach.</param>
/// <param name="DurationSeconds">Length of the recording, in seconds (0–3600).</param>
/// <param name="WrittenNotes">Optional free-form text accompanying the voice note (max 4000 chars).</param>
/// <param name="RubricJson">Optional rubric-criterion summary JSON (max 8000 chars). Defaults to "{}" if omitted.</param>
public sealed record SpeakingReviewVoiceNoteCreateRequest(
    string MediaAssetId,
    int DurationSeconds,
    string? WrittenNotes,
    string? RubricJson);
