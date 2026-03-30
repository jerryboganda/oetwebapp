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

        // Metrics
        expert.MapGet("/metrics", async (HttpContext http, ExpertService service, CancellationToken ct, int? days)
            => Results.Ok(await service.GetMetricsAsync(http.ExpertId(), days ?? 7, ct)));

        return app;
    }

    private static string ExpertId(this HttpContext httpContext)
        => httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Authenticated expert id is required.");
}
