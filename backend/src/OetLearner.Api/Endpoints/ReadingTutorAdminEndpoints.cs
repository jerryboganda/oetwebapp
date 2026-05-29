using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using OetLearner.Api.Services.Reading;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Wave 2 — privileged Reading tutor endpoints (manual override,
/// accepted-answer recalculation, non-redacted attempt review, attempt
/// feedback CRUD, and the assignment workflow). The same surface is exposed
/// under the admin route group (gated by <c>AdminContentWrite</c>) and the
/// expert route group (gated by the existing <c>ExpertOnly</c> policy that
/// tutors already use), plus a learner-facing read of active assignments.
///
/// MISSION CRITICAL: scores are converted via <c>OetScoring</c> inside
/// <see cref="ReadingTutorService"/>; this layer only validates input and
/// shapes responses. Every mutation writes an <c>AuditEvent</c>.
/// </summary>
public static class ReadingTutorAdminEndpoints
{
    public static IEndpointRouteBuilder MapReadingTutorAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/v1/admin/reading")
            .RequireAuthorization("AdminOnly")
            .RequireRateLimiting("PerUser");
        MapShared(admin, isAdmin: true);

        var expert = app.MapGroup("/v1/expert/reading")
            .RequireAuthorization("ExpertOnly")
            .RequireRateLimiting("PerUser");
        MapShared(expert, isAdmin: false);

        // Learner-facing: read-only list of the caller's active assignments.
        app.MapGet("/v1/reading/assignments", async (
            HttpContext http, IReadingTutorService svc, CancellationToken ct) =>
        {
            var userId = CurrentUserId(http);
            return Results.Ok(await svc.ListActiveAssignmentsForLearnerAsync(userId, ct));
        })
        .RequireAuthorization("LearnerOnly")
        .RequireRateLimiting("PerUser");

        return app;
    }

    private static void MapShared(RouteGroupBuilder group, bool isAdmin)
    {
        // ── Manual override ──────────────────────────────────────────────
        Write(group.MapPost("/attempts/{attemptId}/override", async (
            string attemptId,
            ReadingScoreOverrideRequest body,
            HttpContext http,
            IReadingTutorService svc,
            CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Reason))
                return Results.BadRequest(new { error = "reason is required." });
            if (!body.RawScore.HasValue && !body.ScaledScore.HasValue)
                return Results.BadRequest(new { error = "rawScore or scaledScore is required." });

            var review = await svc.ApplyScoreOverrideAsync(attemptId, body, CurrentUserId(http), ct);
            return review is null ? Results.NotFound() : Results.Ok(review);
        }), isAdmin);

        Write(group.MapDelete("/attempts/{attemptId}/override", async (
            string attemptId, HttpContext http, IReadingTutorService svc, CancellationToken ct) =>
        {
            var review = await svc.ClearScoreOverrideAsync(attemptId, CurrentUserId(http), ct);
            return review is null ? Results.NotFound() : Results.Ok(review);
        }), isAdmin);

        // ── Accepted-answer recalculation ────────────────────────────────
        Write(group.MapPost("/papers/{paperId}/recalc", async (
            string paperId,
            ReadingRecalcRequest body,
            HttpContext http,
            IReadingTutorService svc,
            CancellationToken ct) =>
        {
            try
            {
                var result = await svc.RecalcAsync(paperId, body, CurrentUserId(http), ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }), isAdmin);

        // ── Privileged (non-redacted) attempt review ─────────────────────
        Read(group.MapGet("/attempts/{attemptId}", async (
            string attemptId, IReadingTutorService svc, CancellationToken ct) =>
        {
            var review = await svc.GetPrivilegedReviewAsync(attemptId, ct);
            return review is null ? Results.NotFound() : Results.Ok(review);
        }), isAdmin);

        // ── Feedback CRUD ────────────────────────────────────────────────
        Read(group.MapGet("/attempts/{attemptId}/feedback", async (
            string attemptId, IReadingTutorService svc, CancellationToken ct) =>
        {
            return Results.Ok(await svc.ListFeedbackAsync(attemptId, ct));
        }), isAdmin);

        Write(group.MapPost("/attempts/{attemptId}/feedback", async (
            string attemptId,
            ReadingFeedbackRequest body,
            HttpContext http,
            IReadingTutorService svc,
            CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.FeedbackText))
                return Results.BadRequest(new { error = "feedbackText is required." });

            var created = await svc.CreateFeedbackAsync(attemptId, body, CurrentUserId(http), ct);
            return created is null ? Results.NotFound() : Results.Ok(created);
        }), isAdmin);

        Write(group.MapPut("/attempts/{attemptId}/feedback/{feedbackId}", async (
            string attemptId,
            string feedbackId,
            ReadingFeedbackRequest body,
            HttpContext http,
            IReadingTutorService svc,
            CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.FeedbackText))
                return Results.BadRequest(new { error = "feedbackText is required." });

            var updated = await svc.UpdateFeedbackAsync(attemptId, feedbackId, body, CurrentUserId(http), ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        }), isAdmin);

        Write(group.MapDelete("/attempts/{attemptId}/feedback/{feedbackId}", async (
            string attemptId, string feedbackId, HttpContext http, IReadingTutorService svc, CancellationToken ct) =>
        {
            var removed = await svc.DeleteFeedbackAsync(attemptId, feedbackId, CurrentUserId(http), ct);
            return removed ? Results.NoContent() : Results.NotFound();
        }), isAdmin);

        // ── Assignment workflow ──────────────────────────────────────────
        Write(group.MapPost("/assignments", async (
            ReadingAssignmentCreateRequest body,
            HttpContext http,
            IReadingTutorService svc,
            CancellationToken ct) =>
        {
            if (body is null
                || string.IsNullOrWhiteSpace(body.AssignedToUserId)
                || string.IsNullOrWhiteSpace(body.PaperId))
            {
                return Results.BadRequest(new { error = "assignedToUserId and paperId are required." });
            }

            try
            {
                var created = await svc.CreateAssignmentAsync(body, CurrentUserId(http), ct);
                return created is null ? Results.NotFound() : Results.Ok(created);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }), isAdmin);

        Read(group.MapGet("/assignments", async (
            IReadingTutorService svc, CancellationToken ct, [FromQuery] string? assignedToUserId = null) =>
        {
            return Results.Ok(await svc.ListAssignmentsAsync(assignedToUserId, ct));
        }), isAdmin);

        Write(group.MapDelete("/assignments/{id}", async (
            string id, HttpContext http, IReadingTutorService svc, CancellationToken ct) =>
        {
            var cancelled = await svc.CancelAssignmentAsync(id, CurrentUserId(http), ct);
            return cancelled ? Results.NoContent() : Results.NotFound();
        }), isAdmin);
    }

    /// <summary>Apply the write-endpoint policy for the given route group:
    /// admin endpoints layer the granular <c>AdminContentWrite</c> permission
    /// (and write-bucket limit); expert endpoints inherit <c>ExpertOnly</c>
    /// from the group and only add the write rate limit.</summary>
    private static void Write(RouteHandlerBuilder builder, bool isAdmin)
    {
        if (isAdmin) builder.WithAdminWrite("AdminContentWrite");
        else builder.RequireRateLimiting("PerUserWrite");
    }

    /// <summary>Apply the read-endpoint policy. Admin reads layer the
    /// granular permission used by the Reading authoring surface; expert
    /// reads inherit <c>ExpertOnly</c> + the group read limit.</summary>
    private static void Read(RouteHandlerBuilder builder, bool isAdmin)
    {
        if (isAdmin) builder.WithAdminRead("AdminContentWrite");
    }

    private static string CurrentUserId(HttpContext http)
        => http.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated user id is required.");
}
