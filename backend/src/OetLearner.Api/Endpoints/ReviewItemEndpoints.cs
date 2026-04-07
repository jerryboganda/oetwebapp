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
            SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetDueItemsAsync(http.UserId(), limit <= 0 ? 20 : limit, ct)));

        review.MapPost("/items", async (HttpContext http, CreateReviewItemRequest request, SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateReviewItemAsync(http.UserId(), request, ct)));

        review.MapPost("/items/{itemId}/submit", async (
            HttpContext http,
            string itemId,
            ReviewSubmissionRequest request,
            SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.SubmitReviewAsync(http.UserId(), itemId, request.Quality, ct)));

        review.MapDelete("/items/{itemId}", async (HttpContext http, string itemId, SpacedRepetitionService svc, CancellationToken ct) =>
            Results.Ok(await svc.DeleteReviewItemAsync(http.UserId(), itemId, ct)));

        return app;
    }
}

public record ReviewSubmissionRequest(int Quality);

file static class ReviewItemHttpContextExtensions
{
    internal static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
