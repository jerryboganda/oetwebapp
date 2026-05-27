using Microsoft.AspNetCore.Mvc;
using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

/// <summary>
/// Tutor portal for Writing V2. Mounted at /v1/tutors/writing — gated by the
/// existing ExpertOnly auth policy (tutors are platform experts).
/// </summary>
public static class WritingTutorPortalEndpoints
{
    public static IEndpointRouteBuilder MapWritingTutorPortalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/tutors/writing")
            .RequireAuthorization("ExpertOnly")
            .RequireRateLimiting("PerUser");

        group.MapGet("/queue", async (
            [FromQuery] string? status,
            HttpContext http,
            IWritingTutorReviewService service,
            CancellationToken ct)
            => Results.Ok(await service.GetTutorQueueAsync(http.WritingV2UserId(), status, ct)))
            .WithName("GetWritingTutorQueue");

        group.MapPost("/queue/{submissionId:guid}/claim", async (
            Guid submissionId,
            HttpContext http,
            IWritingTutorReviewService service,
            CancellationToken ct) =>
        {
            var claim = await service.ClaimSubmissionForReviewAsync(http.WritingV2UserId(), submissionId, ct);
            return claim is null ? Results.NotFound() : Results.Ok(claim);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("ClaimWritingTutorReview");

        group.MapPost("/reviews", async (
            WritingTutorReviewSubmitRequest request,
            HttpContext http,
            IWritingTutorReviewService service,
            CancellationToken ct) =>
        {
            var review = await service.SubmitTutorReviewAsync(http.WritingV2UserId(), request, ct);
            return Results.Ok(review);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("SubmitWritingTutorReview");

        group.MapGet("/calibration", async (
            HttpContext http,
            IWritingTutorReviewService service,
            CancellationToken ct)
            => Results.Ok(await service.GetTutorCalibrationAsync(http.WritingV2UserId(), ct)))
            .WithName("GetWritingTutorCalibration");

        return app;
    }
}
