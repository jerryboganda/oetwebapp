using System.Security.Claims;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Endpoints;

public static class ListeningLearnerEndpoints
{
    public static IEndpointRouteBuilder MapListeningLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/listening-papers")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

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

        group.MapPost("/attempts/{attemptId}/submit", async (
            string attemptId,
            HttpContext http,
            ListeningLearnerService service,
            CancellationToken ct) =>
            Results.Ok(await service.SubmitAsync(http.UserId(), attemptId, ct)))
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
