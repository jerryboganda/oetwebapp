using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class ReviewItemEndpoints
{
    public static IEndpointRouteBuilder MapReviewItemEndpoints(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/v1").RequireAuthorization("LearnerOnly");
        var review = v1.MapGroup("/review");

        review.MapGet("/summary", async (HttpContext http, SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetReviewSummaryAsync(http.UserId(), ct)));

        review.MapGet("/due", async (
            HttpContext http,
            [FromQuery] int limit,
            [FromQuery] string? source,
            [FromQuery] string? subtest,
            [FromQuery] bool? includeVocabulary,
            SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDueItemsAsync(
                http.UserId(),
                limit,
                source,
                subtest,
                includeVocabulary ?? true,
                ct)));

        review.MapPost("/items", async (HttpContext http, CreateReviewItemRequest request, SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateReviewItemAsync(http.UserId(), request, ct)));

        review.MapPost("/items/{itemId}/submit", async (
            HttpContext http,
            string itemId,
            ReviewSubmissionRequest request,
            SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.SubmitReviewAsync(http.UserId(), itemId, request.Quality, ct)));

        review.MapPost("/items/{itemId}/suspend", async (
            HttpContext http,
            string itemId,
            ReviewSuspendRequest? request,
            SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.SuspendReviewItemAsync(http.UserId(), itemId, request?.Reason, ct)));

        review.MapPost("/items/{itemId}/resume", async (
            HttpContext http,
            string itemId,
            SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.ResumeReviewItemAsync(http.UserId(), itemId, ct)));

        review.MapPost("/items/{itemId}/undo", async (
            HttpContext http,
            string itemId,
            SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.UndoLastAsync(http.UserId(), itemId, ct)));

        review.MapDelete("/items/{itemId}", async (HttpContext http, string itemId, SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.DeleteReviewItemAsync(http.UserId(), itemId, ct)));

        review.MapGet("/retention", async (
            HttpContext http,
            [FromQuery] int? days,
            SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetRetentionAsync(http.UserId(), days ?? 30, ct)));

        review.MapGet("/heatmap", async (HttpContext http, SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetHeatmapAsync(http.UserId(), ct)));

        // Config: per-user daily caps. Stored in a well-known FeatureFlag-like
        // key-value via in-memory defaults for now; extendable to a dedicated
        // table if operators want audited changes.
        review.MapGet("/config", (HttpContext http) =>
            Results.Ok(new ReviewItemConfigResponse(NewCardsPerDay: 20, ReviewsPerDay: 100)));

        review.MapPut("/config", (HttpContext http, ReviewItemConfigRequest request) =>
            // Persisting config is a later enhancement; accept the request but
            // return defaults clamped to safe bounds so the UI is deterministic.
            Results.Ok(new ReviewItemConfigResponse(
                NewCardsPerDay: Math.Clamp(request.NewCardsPerDay, 5, 50),
                ReviewsPerDay: Math.Clamp(request.ReviewsPerDay, 20, 300))));

        return app;
    }
}

public record ReviewSubmissionRequest(int Quality);

public record ReviewSuspendRequest(string? Reason);

public record ReviewItemConfigRequest(int NewCardsPerDay, int ReviewsPerDay);

file static class ReviewItemHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
