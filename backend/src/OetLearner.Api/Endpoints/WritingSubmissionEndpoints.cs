using OetLearner.Api.Contracts;
using OetLearner.Api.Services.Writing;

namespace OetLearner.Api.Endpoints;

public static class WritingSubmissionEndpoints
{
    public static IEndpointRouteBuilder MapWritingSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/writing/submissions")
            .RequireAuthorization("LearnerOnly")
            .RequireRateLimiting("PerUser");

        group.MapPost("/", async (
            WritingSubmissionCreateRequest request,
            HttpContext http,
            IWritingSubmissionService service,
            CancellationToken ct) =>
        {
            var submission = await service.CreateSubmissionAsync(http.WritingV2UserId(), request, ct);
            return Results.Created($"/v1/writing/submissions/{submission.Id}", submission);
        })
        .RequireRateLimiting("writing-submissions-free")
        .WithName("CreateWritingSubmission");

        group.MapGet("/{id:guid}", async (
            Guid id,
            HttpContext http,
            IWritingSubmissionService service,
            CancellationToken ct) =>
        {
            var submission = await service.GetSubmissionAsync(http.WritingV2UserId(), id, ct);
            return submission is null ? Results.NotFound() : Results.Ok(submission);
        })
        .WithName("GetWritingSubmission");

        group.MapGet("/{id:guid}/grade", async (
            Guid id,
            HttpContext http,
            IWritingSubmissionService service,
            CancellationToken ct) =>
        {
            var grade = await service.GetSubmissionGradeAsync(http.WritingV2UserId(), id, ct);
            return grade is null ? Results.NotFound() : Results.Ok(grade);
        })
        .WithName("GetWritingSubmissionGrade");

        group.MapPost("/{id:guid}/revise", async (
            Guid id,
            WritingReviseRequest request,
            HttpContext http,
            IWritingSubmissionService service,
            CancellationToken ct) =>
        {
            var revision = await service.ReviseSubmissionAsync(http.WritingV2UserId(), id, request, ct);
            return revision is null ? Results.NotFound() : Results.Created($"/v1/writing/submissions/{revision.Id}", revision);
        })
        .RequireRateLimiting("writing-submissions-free")
        .WithName("ReviseWritingSubmission");

        group.MapPost("/{id:guid}/appeal", async (
            Guid id,
            WritingAppealRequest? request,
            HttpContext http,
            IWritingAppealService service,
            CancellationToken ct) =>
        {
            var appeal = await service.RequestAppealAsync(http.WritingV2UserId(), id, request?.Reason, ct);
            return appeal is null ? Results.NotFound() : Results.Accepted($"/v1/writing/submissions/{id}/appeal", appeal);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("AppealWritingSubmission");

        group.MapGet("/{id:guid}/exemplar", async (
            Guid id,
            HttpContext http,
            IWritingExemplarService service,
            CancellationToken ct) =>
        {
            var exemplar = await service.GetClosestExemplarForSubmissionAsync(http.WritingV2UserId(), id, ct);
            return exemplar is null ? Results.NotFound() : Results.Ok(exemplar);
        })
        .WithName("GetWritingSubmissionExemplar");

        group.MapPost("/{id:guid}/dispute-violation", async (
            Guid id,
            WritingDisputeViolationRequest request,
            HttpContext http,
            IWritingCanonService service,
            CancellationToken ct) =>
        {
            var updated = await service.DisputeViolationAsync(http.WritingV2UserId(), id, request, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .RequireRateLimiting("PerUserWrite")
        .WithName("DisputeWritingCanonViolation");

        return app;
    }
}
