using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Services.Listening;

namespace OetLearner.Api.Endpoints;

public static class ListeningExpertEndpoints
{
    public static IEndpointRouteBuilder MapListeningExpertEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/expert/listening")
            .RequireAuthorization("ExpertOnly")
            .RequireRateLimiting("PerUser");

        // GET /v1/expert/listening/attempts
        // Returns paginated list of submitted listening attempts the expert can review.
        // `search` matches the learner display name case-insensitively (or an exact
        // user id); `learnerId` remains the exact-id filter it always was.
        group.MapGet("/attempts", async (
            HttpContext http,
            IListeningExpertService service,
            CancellationToken ct,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? learnerId = null,
            [FromQuery] string? paperId = null,
            [FromQuery] string? search = null) =>
        {
            var expertId = http.ListeningExpertId();
            var result = await service.GetAttemptsPagedAsync(expertId, page, pageSize, learnerId, paperId, search, ct);
            return Results.Ok(result);
        });

        // GET /v1/expert/listening/my-reviews
        // Returns paginated list of reviews this expert has already submitted.
        group.MapGet("/my-reviews", async (
            HttpContext http,
            IListeningExpertService service,
            CancellationToken ct,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            var expertId = http.ListeningExpertId();
            var result = await service.GetMyReviewsPagedAsync(expertId, page, pageSize, ct);
            return Results.Ok(result);
        });

        // GET /v1/expert/listening/attempts/{attemptId}/bundle
        // Returns full review bundle: attempt metadata + all answers + transcript evidence + existing feedback.
        group.MapGet("/attempts/{attemptId}/bundle", async (
            string attemptId,
            HttpContext http,
            IListeningExpertService service,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(attemptId))
                return Results.BadRequest("attemptId is required.");

            var expertId = http.ListeningExpertId();
            try
            {
                var bundle = await service.GetReviewBundleAsync(expertId, attemptId, ct);
                return Results.Ok(bundle);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        });

        // GET /v1/expert/listening/attempts/{attemptId}/feedback
        // Returns existing expert feedback for an attempt (null → 404 when none exists).
        group.MapGet("/attempts/{attemptId}/feedback", async (
            string attemptId,
            HttpContext http,
            IListeningExpertService service,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(attemptId))
                return Results.BadRequest("attemptId is required.");

            var expertId = http.ListeningExpertId();
            var feedback = await service.GetFeedbackAsync(expertId, attemptId, ct);
            return feedback is null ? Results.NotFound() : Results.Ok(feedback);
        });

        // POST /v1/expert/listening/attempts/{attemptId}/feedback
        // Submit or update expert feedback. If RawScoreOverride is provided, also
        // writes HumanScoreOverridesJson on the attempt row.
        group.MapPost("/attempts/{attemptId}/feedback", async (
            string attemptId,
            ListeningExpertFeedbackRequest request,
            HttpContext http,
            IListeningExpertService service,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(attemptId))
                return Results.BadRequest("attemptId is required.");

            if (string.IsNullOrWhiteSpace(request.OverallFeedback))
                return Results.BadRequest("overallFeedback is required.");

            // H17: a raw-score override requires a non-empty reason. Reject at
            // the edge so the audit story stays consistent with the service
            // which also throws on this condition.
            if (request.RawScoreOverride.HasValue && string.IsNullOrWhiteSpace(request.ScoreOverrideReason))
                return Results.BadRequest("scoreOverrideReason is required when rawScoreOverride is set.");

            var expertId = http.ListeningExpertId();
            try
            {
                var result = await service.SubmitFeedbackAsync(expertId, attemptId, request, ct);
                return Results.Ok(result);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("listening_override_", System.StringComparison.Ordinal))
            {
                return Results.BadRequest(ex.Message);
            }
        })
        .RequireRateLimiting("PerUserWrite");

        return app;
    }

    private static string ListeningExpertId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated expert id is required.");
}
