using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetWithDrHesham.Api.Services;

namespace OetWithDrHesham.Api.Endpoints;

public static class PeerReviewEndpoints
{
    public static IEndpointRouteBuilder MapPeerReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/community/peer-review")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapPost("/submit", async (
            HttpContext http,
            [FromBody] SubmitPeerReviewRequest req,
            PeerReviewService service,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.SubtestCode) || string.IsNullOrWhiteSpace(req.SubmissionText))
                return Results.BadRequest(new { error = "INVALID_REQUEST" });

            if (req.SubtestCode is not ("writing" or "speaking"))
                return Results.BadRequest(new { error = "INVALID_SUBTEST" });

            var request = await service.SubmitForReviewAsync(
                http.UserId(), req.SubtestCode, req.ContentId ?? "", req.SubmissionText, ct);

            return Results.Ok(new { id = request.Id, status = request.Status, createdAt = request.CreatedAt });
        });

        group.MapGet("/available", async (
            HttpContext http,
            PeerReviewService service,
            CancellationToken ct) =>
        {
            var requests = await service.GetAvailableReviewsAsync(http.UserId(), ct);
            return Results.Ok(requests.Select(r => new
            {
                id = r.Id,
                subtestCode = r.SubtestCode,
                status = r.Status,
                createdAt = r.CreatedAt
            }));
        });

        group.MapPost("/{requestId}/claim", async (
            HttpContext http,
            string requestId,
            PeerReviewService service,
            CancellationToken ct) =>
        {
            var request = await service.ClaimReviewAsync(http.UserId(), requestId, ct);
            if (request == null)
                return Results.BadRequest(new { error = "CANNOT_CLAIM" });

            return Results.Ok(new { id = request.Id, status = request.Status, claimedAt = request.ClaimedAt });
        });

        group.MapPost("/{requestId}/feedback", async (
            HttpContext http,
            string requestId,
            [FromBody] SubmitPeerFeedbackRequest req,
            PeerReviewService service,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.FeedbackText) || req.Rating < 1 || req.Rating > 5)
                return Results.BadRequest(new { error = "INVALID_FEEDBACK" });

            var feedback = await service.SubmitFeedbackAsync(
                http.UserId(), requestId, req.FeedbackText, req.Rating, ct);

            if (feedback == null)
                return Results.BadRequest(new { error = "CANNOT_SUBMIT_FEEDBACK" });

            return Results.Ok(new { id = feedback.Id, rating = feedback.OverallRating, createdAt = feedback.CreatedAt });
        });

        group.MapGet("/my-submissions", async (
            HttpContext http,
            PeerReviewService service,
            CancellationToken ct) =>
        {
            var requests = await service.GetMySubmissionsAsync(http.UserId(), ct);
            var requestIds = requests.Select(r => r.Id).ToList();

            var submissions = new List<object>();
            foreach (var r in requests)
            {
                var feedback = r.Status == "completed"
                    ? await service.GetFeedbackForRequestAsync(r.Id, ct)
                    : null;

                submissions.Add(new
                {
                    id = r.Id,
                    subtestCode = r.SubtestCode,
                    status = r.Status,
                    createdAt = r.CreatedAt,
                    completedAt = r.CompletedAt,
                    feedback = feedback == null ? null : new
                    {
                        id = feedback.Id,
                        comments = feedback.Comments,
                        rating = feedback.OverallRating,
                        strengthNotes = feedback.StrengthNotes,
                        improvementNotes = feedback.ImprovementNotes,
                        createdAt = feedback.CreatedAt
                    }
                });
            }

            return Results.Ok(submissions);
        });

        group.MapGet("/my-reviews", async (
            HttpContext http,
            PeerReviewService service,
            CancellationToken ct) =>
        {
            var reviews = await service.GetMyReviewsAsync(http.UserId(), ct);
            return Results.Ok(reviews.Select(r => new
            {
                id = r.Id,
                subtestCode = r.SubtestCode,
                status = r.Status,
                claimedAt = r.ClaimedAt,
                completedAt = r.CompletedAt
            }));
        });

        return app;
    }

    private static string UserId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}

public record SubmitPeerReviewRequest(string SubtestCode, string? ContentId, string SubmissionText);
public record SubmitPeerFeedbackRequest(string FeedbackText, int Rating);
