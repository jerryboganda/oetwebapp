using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services;

namespace OetLearner.Api.Endpoints;

public static class ExpertEndpoints
{
    public static IEndpointRouteBuilder MapExpertEndpoints(this IEndpointRouteBuilder app)
    {
        var expert = app.MapGroup("/v1/expert")
            .RequireAuthorization("ExpertOnly")
            .RequireRateLimiting("PerUser");

        // Identity
        expert.MapGet("/me", async (HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetMeAsync(http.ExpertId(), ct)));

        expert.MapGet("/dashboard", async (HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetDashboardAsync(http.ExpertId(), ct)));

        // Queue
        expert.MapGet("/queue", async ([AsParameters] ExpertQueueQueryRequest request, HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetQueueAsync(http.ExpertId(), request, ct)));

        expert.MapGet("/queue/filters/metadata", async (HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetQueueFilterMetadataAsync(http.ExpertId(), ct)));

        expert.MapPost("/queue/{reviewRequestId}/claim", async (string reviewRequestId, HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.ClaimReviewAsync(reviewRequestId, http.ExpertId(), ct)))
            .RequireRateLimiting("PerUserWrite");

        expert.MapPost("/queue/{reviewRequestId}/release", async (string reviewRequestId, HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.ReleaseReviewAsync(reviewRequestId, http.ExpertId(), ct)))
            .RequireRateLimiting("PerUserWrite");

        // Review bundles
        expert.MapGet("/reviews/{reviewRequestId}/writing", async (string reviewRequestId, HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetWritingReviewBundleAsync(reviewRequestId, http.ExpertId(), ct)));

        expert.MapGet("/reviews/{reviewRequestId}/speaking", async (string reviewRequestId, HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetSpeakingReviewBundleAsync(reviewRequestId, http.ExpertId(), ct)));

        expert.MapGet("/reviews/{reviewRequestId}/speaking/audio", async (string reviewRequestId, HttpContext http, ExpertService service, CancellationToken ct) =>
        {
            var file = await service.GetSpeakingReviewAudioAsync(reviewRequestId, http.ExpertId(), ct);
            return Results.File(file.Stream, file.ContentType, enableRangeProcessing: true);
        });

        // Draft
        expert.MapPut("/reviews/{reviewRequestId}/draft", async (string reviewRequestId, HttpContext http, ExpertDraftSaveRequest request, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.SaveDraftAsync(reviewRequestId, http.ExpertId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        // Submit
        expert.MapPost("/reviews/{reviewRequestId}/writing/submit", async (string reviewRequestId, HttpContext http, ExpertReviewSubmitRequest request, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.SubmitWritingReviewAsync(reviewRequestId, http.ExpertId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        expert.MapPost("/reviews/{reviewRequestId}/speaking/submit", async (string reviewRequestId, HttpContext http, ExpertReviewSubmitRequest request, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.SubmitSpeakingReviewAsync(reviewRequestId, http.ExpertId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        // Rework
        expert.MapPost("/reviews/{reviewRequestId}/rework", async (string reviewRequestId, HttpContext http, ExpertReworkRequest request, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.RequestReworkAsync(reviewRequestId, http.ExpertId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        expert.MapGet("/learners", async ([AsParameters] ExpertLearnersQueryRequest request, HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetLearnersAsync(http.ExpertId(), request, ct)));

        // Learner profile
        expert.MapGet("/learners/{learnerId}", async (string learnerId, HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetLearnerProfileAsync(learnerId, http.ExpertId(), ct)));

        // Learner review-context (lightweight)
        expert.MapGet("/learners/{learnerId}/review-context", async (string learnerId, HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetLearnerReviewContextAsync(learnerId, http.ExpertId(), ct)));

        // Review history
        expert.MapGet("/reviews/{reviewRequestId}/history", async (string reviewRequestId, HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetReviewHistoryAsync(reviewRequestId, http.ExpertId(), ct)));

        // Calibration
        expert.MapGet("/calibration/cases", async (HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetCalibrationCasesAsync(http.ExpertId(), ct)));

        expert.MapGet("/calibration/cases/{caseId}", async (string caseId, HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetCalibrationCaseDetailAsync(caseId, http.ExpertId(), ct)));

        expert.MapGet("/calibration/notes", async (HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetCalibrationNotesAsync(http.ExpertId(), ct)));

        expert.MapPost("/calibration/cases/{caseId}/submit", async (string caseId, HttpContext http, ExpertCalibrationSubmitRequest request, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.SubmitCalibrationAsync(caseId, http.ExpertId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        // Schedule / Availability
        expert.MapGet("/schedule", async (HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetAvailabilityAsync(http.ExpertId(), ct)));

        expert.MapPut("/schedule", async (HttpContext http, ExpertAvailabilityUpdateRequest request, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.SaveAvailabilityAsync(http.ExpertId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        // Schedule exceptions
        expert.MapPost("/schedule/exceptions", async (HttpContext http, CreateScheduleExceptionRequest request, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.CreateScheduleExceptionAsync(http.ExpertId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        expert.MapGet("/schedule/exceptions", async (HttpContext http, ExpertService service, CancellationToken ct,
            [FromQuery] string? from, [FromQuery] string? to) =>
        {
            DateOnly? fromDate = DateOnly.TryParseExact(from, "yyyy-MM-dd", out var f) ? f : null;
            DateOnly? toDate = DateOnly.TryParseExact(to, "yyyy-MM-dd", out var t) ? t : null;
            return Results.Ok(await service.GetScheduleExceptionsAsync(http.ExpertId(), fromDate, toDate, ct));
        });

        expert.MapDelete("/schedule/exceptions/{exceptionId}", async (string exceptionId, HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.DeleteScheduleExceptionAsync(http.ExpertId(), exceptionId, ct)))
            .RequireRateLimiting("PerUserWrite");

        // Metrics
        expert.MapGet("/metrics", async (HttpContext http, ExpertService service, CancellationToken ct, int? days)
            => Results.Ok(await service.GetMetricsAsync(http.ExpertId(), days ?? 7, ct)));

        // ── Annotation Templates ────────────────────────

        expert.MapGet("/annotation-templates", async (HttpContext http, ExpertService service, CancellationToken ct, string? subtestCode, string? criterionCode)
            => Results.Ok(await service.GetAnnotationTemplatesAsync(http.ExpertId(), subtestCode, criterionCode, ct)));

        expert.MapPost("/annotation-templates", async (HttpContext http, ExpertAnnotationTemplateRequest request, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.CreateAnnotationTemplateAsync(http.ExpertId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        expert.MapPut("/annotation-templates/{templateId}", async (string templateId, HttpContext http, ExpertAnnotationTemplateRequest request, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.UpdateAnnotationTemplateAsync(templateId, http.ExpertId(), request, ct)))
            .RequireRateLimiting("PerUserWrite");

        expert.MapDelete("/annotation-templates/{templateId}", async (string templateId, HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.DeleteAnnotationTemplateAsync(templateId, http.ExpertId(), ct)))
            .RequireRateLimiting("PerUserWrite");

        // ── X3: Scoring Quality Metrics ─────────────────

        expert.MapGet("/scoring-quality", async (HttpContext http, [FromQuery] int? days, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetScoringQualityMetricsAsync(http.ExpertId(), days ?? 30, ct)));

        // ── XE1: Queue Priority Visibility ──────────────

        expert.MapGet("/queue-priority", async (HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetQueueWithPriorityReasonsAsync(http.ExpertId(), ct)));

        // ── XE2: AI Pre-Fill for Reviews ────────────────

        expert.MapGet("/reviews/{reviewRequestId}/ai-prefill", async (string reviewRequestId, HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetAiPreFillForReviewAsync(http.ExpertId(), reviewRequestId, ct)));

        // ── E7: Expert Q&A — Verified Reply to Community Thread ─────────

        expert.MapGet("/community/ask-an-expert", async (
            [FromQuery] int page, [FromQuery] int pageSize,
            ExpertService service, CancellationToken ct)
            => Results.Ok(await service.GetAskAnExpertThreadsAsync(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize, ct)));

        expert.MapPost("/community/threads/{threadId}/verified-reply", async (
            string threadId, ExpertVerifiedReplyRequest req,
            HttpContext http, ExpertService service, CancellationToken ct)
            => Results.Ok(await service.PostVerifiedReplyAsync(http.ExpertId(), threadId, req.Body, ct)));

        return app;
    }

    private static string ExpertId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated expert id is required.");
}

public record ExpertVerifiedReplyRequest(string Body);
