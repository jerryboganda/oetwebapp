using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

// Wave 4 of docs/SPEAKING-MODULE-PLAN.md.
//
// Three concerns wired up here:
//   1. Tutor calibration  — list samples + submit rubric (`/v1/expert/...`).
//   2. Inline transcript comments by experts (`/v1/expert/...`).
//   3. Inline transcript comments READ for learners + experts + admins
//      (`/v1/speaking/attempts/{attemptId}/comments`).
//
// Permissions:
//   - Expert routes use the existing `ExpertOnly` policy (already in
//     `AuthorizationPolicies.cs`).
//   - Learner-side read uses `LearnerOnly` and the service enforces
//     attempt ownership; admins/experts who hit the same path get a
//     superset view because the service inspects role flags.
public static class SpeakingCalibrationEndpoints
{
    public static IEndpointRouteBuilder MapSpeakingCalibrationEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Expert / tutor surface ──
        var expert = app.MapGroup("/v1/expert/calibration/speaking")
            .RequireAuthorization("ExpertOnly")
            .WithTags("Speaking calibration");

        expert.MapGet("/samples", async (HttpContext http, SpeakingTutorCalibrationService svc, CancellationToken ct)
            => Results.Ok(await svc.ListPublishedSamplesForTutorAsync(http.ExpertId(), ct)))
            .WithSummary("List published calibration samples plus the tutor's own latest drift score for each.");

        expert.MapPost("/samples/{sampleId}/scores", async (
            string sampleId, HttpContext http,
            TutorSpeakingCalibrationSubmitRequest req,
            SpeakingTutorCalibrationService svc, CancellationToken ct)
            => Results.Ok(await svc.SubmitCalibrationScoresAsync(http.ExpertId(), sampleId, req, ct)))
            .WithSummary("Tutor submits or re-submits the 9-criterion rubric for a calibration sample.");

        var expertComments = app.MapGroup("/v1/expert/speaking/attempts")
            .RequireAuthorization("ExpertOnly")
            .WithTags("Speaking calibration");

        expertComments.MapPost("/{attemptId}/comments", async (
            string attemptId, HttpContext http,
            ExpertSpeakingFeedbackCommentRequest req,
            SpeakingTutorCalibrationService svc, CancellationToken ct)
            => Results.Ok(await svc.PostInlineCommentAsync(http.ExpertId(), attemptId, req, ct)))
            .WithSummary("Expert posts an inline comment under a transcript line.");

        // ── Learner / shared read surface ──
        // /v1/speaking/attempts/{attemptId}/comments — readable by:
        //   - the attempt owner (learner)
        //   - any authenticated expert
        //   - any authenticated admin
        // Anything else → 403.
        app.MapGet("/v1/speaking/attempts/{attemptId}/comments", async (
            string attemptId, HttpContext http,
            SpeakingTutorCalibrationService svc, CancellationToken ct)
            => Results.Ok(await svc.ListCommentsForAttemptAsync(
                http.AuthenticatedUserId(),
                isExpert: http.User.IsInRole("expert"),
                isAdmin: http.User.IsInRole("admin"),
                attemptId, ct)))
            .RequireAuthorization()
            .WithTags("Speaking calibration")
            .WithSummary("List inline comments on a speaking attempt's transcript.");

        return app;
    }

    private static string ExpertId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated expert id is required.");

    private static string AuthenticatedUserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
