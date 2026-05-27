using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

public static class WritingTutorReviewLearnerEndpoints
{
    public static IEndpointRouteBuilder MapWritingTutorReviewLearnerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/submissions")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapPost("/{id:guid}/request-tutor-review", async (
            Guid id,
            WritingTutorReviewRequestPayload? request,
            HttpContext http,
            IWritingTutorReviewService service,
            CancellationToken ct) =>
        {
            var review = await service.RequestTutorReviewAsync(http.WritingV2UserId(), id, request?.Priority, ct);
            return review is null ? Results.NotFound() : Results.Accepted($"/v1/writing/submissions/{id}/tutor-review", review);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("RequestWritingTutorReview");

        group.MapGet("/{id:guid}/tutor-review", async (
            Guid id,
            HttpContext http,
            IWritingTutorReviewService service,
            CancellationToken ct) =>
        {
            var review = await service.GetTutorReviewAsync(http.WritingV2UserId(), id, ct);
            return review is null ? Results.Ok((WritingTutorReviewResponse?)null) : Results.Ok(review);
        })
        .WithName("GetWritingTutorReview");

        return app;
    }
}
