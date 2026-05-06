using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Endpoints;

public static class ListeningLearnerEndpoints
{
    public sealed record ListeningSubmitRequest(Dictionary<string, string?>? Answers);

    public static IEndpointRouteBuilder MapListeningLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/listening-papers")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        // ── Course pathway snapshot (diagnostic → drills → mocks → ready) ──
        group.MapGet("/me/pathway", async (
            IListeningPathwayService pathway, HttpContext http, CancellationToken ct) =>
        {
            var snap = await pathway.GetPathwayAsync(http.UserId(), ct);
            return Results.Ok(snap);
        })
            .WithName("GetListeningPathway")
            .WithSummary("Get the learner's Listening course pathway snapshot");

        // ── Phase 6: per-learner Listening analytics ──
        group.MapGet("/me/analytics", async (
            IListeningAnalyticsService analytics, HttpContext http, CancellationToken ct) =>
        {
            var data = await analytics.GetMyAnalyticsAsync(http.UserId(), ct);
            return Results.Ok(data);
        })
            .WithName("GetListeningStudentAnalytics")
            .WithSummary("Per-learner Listening analytics: per-part accuracy, top weaknesses, action plan");

        // ── Phase 10: 12-stage Listening curriculum metadata ──
        group.MapGet("/me/curriculum", async (
            IListeningCurriculumService curriculum, HttpContext http, CancellationToken ct) =>
        {
            var data = await curriculum.GetCurriculumAsync(http.UserId(), ct);
            return Results.Ok(data);
        })
            .WithName("GetListeningCurriculum")
            .WithSummary("12-stage Listening curriculum + per-stage completion");

        group.MapGet("/papers/{paperId}/session", async (
            string paperId,
            string? mode,
            string? attemptId,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetSessionAsync(http.UserId(), paperId, mode, attemptId, ct)))
            .WithName("GetListeningPaperSession")
            .WithSummary("Get a learner-safe Listening paper session")
            .WithDescription("Returns Listening audio metadata, learner-safe questions, policy, and optional attempt state without exposing answer keys before submit.");

        group.MapPost("/papers/{paperId}/attempts", async (
            string paperId,
            ListeningAttemptStartRequest request,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.StartAttemptAsync(http.UserId(), paperId, request.Mode, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("StartListeningPaperAttempt")
            .WithSummary("Start or resume a Listening paper attempt");

        group.MapGet("/attempts/{attemptId}", async (
            string attemptId,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetAttemptAsync(http.UserId(), attemptId, ct)))
            .WithName("GetListeningPaperAttempt")
            .WithSummary("Get a Listening attempt for resume state");

        group.MapPut("/attempts/{attemptId}/answers/{questionId}", async (
            string attemptId,
            string questionId,
            ListeningAnswerSaveRequest request,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
        {
            await service.SaveAnswerAsync(http.UserId(), attemptId, questionId, request, ct);
            return Results.NoContent();
        })
            .RequireRateLimiting("PerUserWrite")
            .WithName("SaveListeningPaperAnswer")
            .WithSummary("Autosave one Listening answer");

        group.MapPatch("/attempts/{attemptId}/heartbeat", async (
            string attemptId,
            HeartbeatRequest request,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.HeartbeatAsync(http.UserId(), attemptId, request, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("HeartbeatListeningPaperAttempt")
            .WithSummary("Persist Listening attempt playback/activity heartbeat");

        group.MapPost("/attempts/{attemptId}/integrity-events", async (
            string attemptId,
            ListeningIntegrityEventRequest request,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
        {
            await service.RecordIntegrityEventAsync(http.UserId(), attemptId, request, ct);
            return Results.NoContent();
        })
            .RequireRateLimiting("PerUserWrite")
            .WithName("RecordListeningIntegrityEvent")
            .WithSummary("Record an OET@Home Listening integrity event");

        group.MapPost("/attempts/{attemptId}/submit", async (
            string attemptId,
            ListeningSubmitRequest? request,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.SubmitAsync(http.UserId(), attemptId, request?.Answers, ct)))
            .RequireRateLimiting("PerUserWrite")
            .WithName("SubmitListeningPaperAttempt")
            .WithSummary("Submit and server-grade a Listening attempt");

        group.MapGet("/attempts/{attemptId}/review", async (
            string attemptId,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetReviewAsync(http.UserId(), attemptId, ct)))
            .WithName("GetListeningPaperReview")
            .WithSummary("Get policy-safe Listening result and transcript-backed review");

        group.MapGet("/drills/{drillId}", async (
            string drillId,
            string? paperId,
            string? attemptId,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetDrillAsync(drillId, paperId, attemptId, ct)))
            .WithName("GetListeningPaperDrill")
            .WithSummary("Get a Listening drill recommendation");

        return app;
    }

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}

public sealed record ListeningAttemptStartRequest(string? Mode);
