using System.Security.Claims;
using OetWithDrHesham.Api.Contracts;
using OetWithDrHesham.Api.Services.Content;
using OetWithDrHesham.Api.Services.Speaking;

namespace OetWithDrHesham.Api.Endpoints;

// Phase 4 (B.4) of the OET Speaking module roadmap.
//
// All eight tutor-facing speaking routes plus the single learner-facing
// dual-assessment GET:
//
//   POST   /v1/expert/speaking/sessions/{id}/tutor-assessment
//   PATCH  /v1/expert/speaking/sessions/{id}/tutor-assessments/{aid}
//   POST   /v1/expert/speaking/sessions/{id}/tutor-assessments/{aid}/submit
//   POST   /v1/expert/speaking/sessions/{id}/comments
//   GET    /v1/expert/speaking/sessions/{id}/assessments
//   GET    /v1/expert/speaking/sessions/{id}/context
//   GET    /v1/expert/speaking/sessions/{id}/recording
//   GET    /v1/expert/speaking/queue?professionId=
//   POST   /v1/expert/speaking/queue/{sessionId}/claim
//   POST   /v1/expert/speaking/queue/{sessionId}/release
//   GET    /v1/speaking/sessions/{id}/assessments       (learner dual)
//
// Authorization:
//   * /v1/expert/speaking/*   → ExpertOnly policy
//   * /v1/speaking/sessions/* → LearnerOnly policy
public static class TutorSpeakingEndpoints
{
    public static IEndpointRouteBuilder MapTutorSpeakingEndpoints(this IEndpointRouteBuilder app)
    {
        var expert = app.MapGroup("/v1/expert/speaking")
            .RequireAuthorization("ExpertOnly");

        expert.MapPost("/sessions/{id}/tutor-assessment", async (
            HttpContext http,
            string id,
            TutorAssessmentDraftRequest body,
            TutorAssessmentService svc,
            CancellationToken ct) =>
        {
            var assessmentId = await svc.CreateDraftAsync(http.ExpertId(), id, body, ct);
            return Results.Created(
                $"/v1/expert/speaking/sessions/{id}/tutor-assessments/{assessmentId}",
                new { id = assessmentId });
        });

        expert.MapPatch("/sessions/{id}/tutor-assessments/{aid}", async (
            HttpContext http,
            string id,
            string aid,
            TutorAssessmentDraftRequest body,
            TutorAssessmentService svc,
            CancellationToken ct) =>
        {
            await svc.UpdateDraftAsync(http.ExpertId(), id, aid, body, ct);
            return Results.NoContent();
        });

        expert.MapPost("/sessions/{id}/tutor-assessments/{aid}/submit", async (
            HttpContext http,
            string id,
            string aid,
            TutorAssessmentSubmitRequest body,
            TutorAssessmentService svc,
            CancellationToken ct) =>
        {
            var projection = await svc.SubmitAsync(http.ExpertId(), id, aid, body, ct);
            return Results.Ok(projection);
        });

        expert.MapPost("/sessions/{id}/comments", async (
            HttpContext http,
            string id,
            TutorTimestampedCommentRequest body,
            TutorAssessmentService svc,
            CancellationToken ct) =>
        {
            var commentId = await svc.AddTimestampedCommentAsync(http.ExpertId(), id, body, ct);
            return Results.Created(
                $"/v1/expert/speaking/sessions/{id}/comments/{commentId}",
                new { id = commentId });
        });

        expert.MapGet("/sessions/{id}/assessments", async (
            HttpContext http,
            string id,
            TutorAssessmentService svc,
            CancellationToken ct) =>
        {
            var dual = await svc.GetDualAssessmentForTutorAsync(http.ExpertId(), id, ct);
            return Results.Ok(dual);
        });

        expert.MapGet("/sessions/{id}/context", async (
            HttpContext http,
            string id,
            TutorAssessmentService svc,
            CancellationToken ct) =>
        {
            var context = await svc.GetSessionContextForTutorAsync(http.ExpertId(), id, ct);
            return Results.Ok(context);
        });

        expert.MapGet("/sessions/{id}/recording", async (
            HttpContext http,
            string id,
            TutorAssessmentService svc,
            SpeakingComplianceService compliance,
            IFileStorage storage,
            CancellationToken ct) =>
        {
            var recording = await svc.GetRecordingForTutorAsync(http.ExpertId(), id, ct);
            await compliance.AdminAccessRecordingAsync(
                http.ExpertId(),
                http.ExpertId(),
                recording.Id,
                "speaking_tutor_assessment_playback",
                ct);
            var media = recording.MediaAsset!;
            var stream = await storage.OpenReadAsync(media.StoragePath, ct);
            return Results.File(stream, media.MimeType, enableRangeProcessing: true);
        });

        expert.MapGet("/queue", async (
            HttpContext http,
            string? professionId,
            TutorReviewQueueService svc,
            CancellationToken ct) =>
        {
            var items = await svc.ListQueueAsync(http.ExpertId(), professionId, ct);
            return Results.Ok(new { items });
        });

        expert.MapPost("/queue/{sessionId}/claim", async (
            HttpContext http,
            string sessionId,
            TutorReviewQueueService svc,
            CancellationToken ct) =>
        {
            await svc.ClaimAsync(http.ExpertId(), sessionId, ct);
            return Results.NoContent();
        });

        expert.MapPost("/queue/{sessionId}/release", async (
            HttpContext http,
            string sessionId,
            TutorReviewQueueService svc,
            CancellationToken ct) =>
        {
            await svc.ReleaseAsync(http.ExpertId(), sessionId, ct);
            return Results.NoContent();
        });

        // Learner-facing dual assessment endpoint. The two tracks are
        // returned side-by-side; ownership of the underlying session is
        // enforced by the service so a learner cannot read another
        // learner's scores.
        var learner = app.MapGroup("/v1/speaking")
            .RequireAuthorization("LearnerOnly");

        learner.MapGet("/sessions/{id}/assessments", async (
            HttpContext http,
            string id,
            TutorAssessmentService svc,
            CancellationToken ct) =>
        {
            var dual = await svc.GetDualAssessmentForLearnerAsync(http.LearnerId(), id, ct);
            return Results.Ok(dual);
        });

        return app;
    }

    private static string ExpertId(this HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated expert id is required.");

    private static string LearnerId(this HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated learner id is required.");
}
